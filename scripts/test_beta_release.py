"""Offline integration tests: the actual OSS SDK sends requests to loopback only."""
import hashlib
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from tempfile import TemporaryDirectory
from threading import Thread
import unittest

import alibabacloud_oss_v2 as oss
from beta_release import classify_tag, publish_beta


TAG = 'v3.9.0-beta.10'
NOTES = '# 更新\n\n- 测试说明\n'
NAME = f'ASPAssistant-{TAG}-win-x64-setup.exe'
PREFIX = '/fixture/updates/beta/'
LATEST = PREFIX + 'latest.json'
ASSET = PREFIX + NAME


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def answer(self, code, body=b''):
        self.send_response(code)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.server.deny:
            self.answer(403, b'<Error><Code>AccessDenied</Code></Error>')
        elif self.path not in self.server.objects:
            self.answer(404, b'<Error><Code>NoSuchKey</Code></Error>')
        else:
            self.answer(200, self.server.objects[self.path])

    def do_PUT(self):
        if self.path in self.server.fail:
            self.answer(403, b'<Error><Code>AccessDenied</Code></Error>')
            return
        self.server.writes.append((self.path, self.headers))
        self.server.objects[self.path] = self.rfile.read(int(self.headers['Content-Length']))
        self.answer(200)


class BetaReleaseTests(unittest.TestCase):
    def setUp(self):
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.server.objects, self.server.writes = {}, []
        self.server.deny, self.server.fail = False, set()
        thread = Thread(target=self.server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        cfg = oss.config.load_default()
        cfg.region, cfg.use_path_style = 'cn-beijing', True
        cfg.endpoint = f'http://127.0.0.1:{self.server.server_port}'
        cfg.credentials_provider = oss.credentials.StaticCredentialsProvider('test', 'test')
        self.client = oss.Client(cfg)
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.dir = Path(directory.name)
        (self.dir / NAME).write_bytes(b'test installer')
        self.notes = self.dir / 'notes.md'
        self.notes.write_text(NOTES, encoding='utf-8')

    def publish(self, tag=TAG):
        publish_beta(self.client, 'fixture', tag, self.dir, self.notes)

    def test_tag_routing_is_strict(self):
        self.assertEqual('beta', classify_tag(TAG))
        self.assertEqual('stable', classify_tag('v3.9.0'))
        for tag in ('v3.9.0-beta.0', 'v3.09.0', 'v3.9.0-BETA.1', 'v3.9.0-beta.01',
                    'v3.9.0-beta.2147483648', 'v2147483648.0.0', 'v3.9.0-beta.1+g1', 'v3.9.0-rc.1', 'v3.9.0-beta.１', 'v3.9.0\n'):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                classify_tag(tag)

    def test_publish_order_schema_and_actual_bytes(self):
        self.server.objects[LATEST] = json.dumps(dict(tag='v3.9.0-beta.9', notes='old', sha256='0'*64)).encode()
        self.publish()
        self.assertEqual([ASSET, LATEST], [k for k, _ in self.server.writes])
        asset_headers, manifest_headers = [h for _, h in self.server.writes]
        self.assertEqual('true', asset_headers['x-oss-forbid-overwrite'])
        self.assertIn('no-store', manifest_headers['Cache-Control'])
        self.assertEqual('application/json', manifest_headers['Content-Type'].split(';')[0])
        self.assertEqual(dict(tag=TAG, sha256=hashlib.sha256(self.server.objects[ASSET]).hexdigest(), notes=NOTES),
                         json.loads(self.server.objects[LATEST]))

    def test_failed_upload_does_not_announce(self):
        self.server.fail.add(ASSET)
        with self.assertRaises(oss.exceptions.OperationError):
            self.publish()
        self.assertNotIn(LATEST, self.server.objects)

    def test_orphan_from_partial_publish_is_not_replaced(self):
        self.server.objects[ASSET] = b'another build'
        with self.assertRaises(ValueError):
            self.publish()
        self.assertEqual([], self.server.writes)
        self.server.objects[ASSET] = (self.dir / NAME).read_bytes()
        self.publish()
        self.assertEqual([LATEST], [k for k, _ in self.server.writes])

    def test_no_rollback_or_reuse_of_tag(self):
        for tag in (TAG, 'v3.9.0-beta.11', 'v3.10.0-beta.1', 'v3.9.0'):
            with self.subTest(tag=tag):
                self.server.objects[LATEST] = json.dumps(dict(tag=tag, notes='old', sha256='1'*64)).encode()
                with self.assertRaises(ValueError):
                    self.publish()
                self.assertEqual([], self.server.writes)

    def test_read_denied_is_not_first_publish(self):
        self.server.deny = True
        with self.assertRaises(oss.exceptions.OperationError):
            self.publish()
        self.assertEqual([], self.server.writes)
