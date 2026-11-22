"""Classify release tags, or publish one installer to the in-app beta channel."""
import hashlib
import json
import os
from pathlib import Path
import re
import sys


NUMBER = r'(0|[1-9][0-9]*)'
TAG = re.compile(rf'v{NUMBER}\.{NUMBER}\.{NUMBER}(?:-beta\.([1-9][0-9]*))?')
PREFIX = 'updates/beta/'


def parse_tag(tag):
    match = TAG.fullmatch(tag)
    if not match:
        raise ValueError('Expected a canonical vX.Y.Z or vX.Y.Z-beta.N tag (N >= 1).')
    parts = match.groups()
    if any(int(part) > 2147483647 for part in parts if part is not None):
        raise ValueError('Version components must fit the desktop client Int32 parser.')
    return parts


def classify_tag(tag):
    return 'stable' if parse_tag(tag)[3] is None else 'beta'


def beta_order(tag):
    parts = parse_tag(tag)
    if parts[3] is None:
        raise ValueError('The beta feed only accepts beta tags.')
    return tuple(map(int, parts))


def digest(chunks):
    sha = hashlib.sha256()
    for chunk in chunks:
        sha.update(chunk)
    return sha.hexdigest()


def existing_body(client, bucket, key):
    import alibabacloud_oss_v2 as oss

    try:
        response = client.get_object(oss.GetObjectRequest(bucket=bucket, key=key))
    except oss.exceptions.OperationError as error:
        cause = error.unwrap()
        if isinstance(cause, oss.exceptions.ServiceError) and cause.status_code == 404 and cause.code == 'NoSuchKey':
            return None
        raise  # Never interpret broken credentials, connectivity or a missing bucket as an empty feed.
    return response.body


def publish_beta(client, bucket, tag, directory, notes_path):
    import alibabacloud_oss_v2 as oss

    version = beta_order(tag)
    manifest_key = PREFIX + 'latest.json'
    previous = existing_body(client, bucket, manifest_key)
    if previous is not None:
        with previous as body:
            previous_tag = json.loads(body.read())['tag']
        if version <= beta_order(previous_tag):
            raise ValueError(f'Not replacing {previous_tag} with {tag}. Use a new, higher beta number.')

    name = f'ASPAssistant-{tag}-win-x64-setup.exe'
    notes = notes_path.read_text(encoding='utf-8-sig')
    if not notes.strip():
        raise ValueError('Beta release notes are empty.')

    with (directory / name).open('rb') as installer:
        sha256 = hashlib.file_digest(installer, 'sha256').hexdigest()
        installer.seek(0)
        existing = existing_body(client, bucket, PREFIX + name)
        if existing is not None:
            # A failed manifest write may have left an orphan. Only reuse byte-identical content.
            with existing as body:
                if digest(body.iter_bytes()) != sha256:
                    raise ValueError(f'Different installer already exists for {tag}. Use a new beta number.')
        else:
            client.put_object(oss.PutObjectRequest(
                bucket=bucket, key=PREFIX + name, body=installer, forbid_overwrite=True,
                content_type='application/octet-stream', cache_control='public, max-age=31536000, immutable',
            ))

    # OSS object writes are atomic. CI serializes beta writers; never announce a failed upload.
    manifest = json.dumps(dict(tag=tag, sha256=sha256, notes=notes), ensure_ascii=False).encode('utf-8')
    client.put_object(oss.PutObjectRequest(
        bucket=bucket, key=manifest_key, body=manifest,
        content_type='application/json; charset=utf-8', cache_control='no-store, max-age=0',
    ))


def main():
    tag = os.environ['GITHUB_REF_NAME']
    if sys.argv[1:] == ['classify']:
        channel = classify_tag(tag)
        with Path(os.environ['GITHUB_ENV']).open('a', encoding='utf-8') as env:
            print(f'VERSION={tag[1:]}\nRELEASE_CHANNEL={channel}', file=env)
        return
    if sys.argv[1:] != ['publish']:
        raise SystemExit('Usage: beta_release.py {classify|publish}')

    import alibabacloud_oss_v2 as oss

    # The desktop client pins this public OSS origin; do not silently publish somewhere else.
    if os.environ['OSS_BUCKET'] != 'aspa' or os.environ['OSS_ENDPOINT'] != 'oss-cn-beijing.aliyuncs.com':
        raise ValueError('Configured OSS bucket/endpoint do not match the in-app beta origin.')
    cfg = oss.config.load_default()
    cfg.region = 'cn-beijing'
    cfg.credentials_provider = oss.credentials.EnvironmentVariableCredentialsProvider()
    publish_beta(oss.Client(cfg), 'aspa', tag, Path('publish'), Path('RELEASE_NOTES.md'))
    print(f'Published in-app beta {tag}.')


if __name__ == '__main__':
    main()
