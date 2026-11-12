import assert from 'node:assert/strict';
import test from 'node:test';
import * as format from '../web/diagram-format.js';

const videoUrl = 'https://www.bilibili.com/video/BV1abc/?p=2&t=70';
const graph = ['```lineup', 'flowchart TD', 'A[{{op:地灵}}]', '```'].join('\n');

test('阵容介绍分离原视频和流程图，保留其余介绍', () => {
  assert.equal(typeof format.splitLineupDescription, 'function');
  const description = ['# 核心循环', '', '```aspa-source', '原作者', videoUrl, '```', '', graph.replaceAll('\n', '\r\n')]
    .join('\r\n');
  assert.deepEqual(format.splitLineupDescription(description), {
    prose: '# 核心循环', diagrams: graph.replaceAll('\n', '\r\n'), originalAuthor: '原作者', originalVideoUrl: videoUrl
  });
});

test('示例和错误格式保留为正文，只有首个顶层原视频块被提取', () => {
  assert.equal(typeof format.splitLineupDescription, 'function');
  const nested = ['````text', '```aspa-source', '示例作者', videoUrl, '```', '````'].join('\n');
  const invalid = ['```aspa-source', '缺一行', '```'].join('\n');
  const indented = [' ```aspa-source', '缩进示例', videoUrl, ' ```'].join('\n');
  const duplicate = ['```aspa-source', '重复原作者', videoUrl, '```'].join('\n');
  const earlier = [nested, invalid, indented].join('\n\n');
  const description = [earlier, ['```aspa-source', '原作者', videoUrl, '```'].join('\n'), duplicate].join('\n\n');
  assert.deepEqual(format.splitLineupDescription(description), {
    prose: earlier + '\n\n' + duplicate, diagrams: '', originalAuthor: '原作者', originalVideoUrl: videoUrl
  });
  const unclosed = '说明\n```aspa-source\n原作者\n' + videoUrl;
  assert.deepEqual(format.splitLineupDescription(unclosed), {
    prose: unclosed, diagrams: '', originalAuthor: '', originalVideoUrl: ''
  });
});

test('编辑原视频时按双行格式保存单独填写的字段，清空两项后移除原视频块', () => {
  assert.equal(typeof format.combineLineupDescription, 'function');
  assert.equal(format.combineLineupDescription('打法', graph, '', videoUrl),
    ['打法', '', '```aspa-source', '', videoUrl, '```', '', graph].join('\n'));
  assert.equal(format.combineLineupDescription('打法', graph, '原作者', ''),
    ['打法', '', '```aspa-source', '原作者', '', '```', '', graph].join('\n'));
  assert.equal(format.combineLineupDescription('打法', graph, '', ''), '打法\n\n' + graph);
});

test('编辑后的原视频在未闭合的代码示例前仍可读取', () => {
  const prose = '打法\n\n```text\n还未完成的示例';
  const description = format.combineLineupDescription(prose, '', '原作者', videoUrl);
  assert.deepEqual(format.splitLineupDescription(description), {
    prose, diagrams: '', originalAuthor: '原作者', originalVideoUrl: videoUrl
  });
});

test('仅将 HTTPS 哔哩哔哩视频和短链接转为可点击地址', () => {
  assert.equal(typeof format.bilibiliVideoHref, 'function');
  for (const valid of [videoUrl, 'https://m.bilibili.com/video/av123', 'https://bilibili.com/video/BV1abc',
    'https://b23.tv/Ab12?share_source=copy_web', 'https://WWW.BILIBILI.COM:443/video/BV1abc']) {
    assert.equal(format.bilibiliVideoHref(valid), new URL(valid).href);
  }
  for (const invalid of ['http://www.bilibili.com/video/BV1abc', 'javascript:alert(1)', 'https:b23.tv/Ab12',
    'https://www.bilibili.com.evil.test/video/BV1abc', 'https://www.bilibili.com@evil.test/video/BV1abc',
    'https://user:pass@www.bilibili.com/video/BV1abc', 'https://@www.bilibili.com/video/BV1abc',
    'https://www.bilibili.com:444/video/BV1abc', 'https://www.bilibili.com/search?keyword=BV1abc',
    'https://www.bilibili.com/video/BV1abc/extra', 'https://bilibili.com/video/other/../av42',
    'https://b23.tv/', 'https://b23.tv/Ab12/extra']) {
    assert.equal(format.bilibiliVideoHref(invalid), '', invalid);
  }
});

test('阵容来源接受森空岛卫戍协议攻略直链及分享参数', () => {
  assert.equal(typeof format.lineupSourceHref, 'function');
  const guide = 'https://game.skland.com/arknights/stronghold-guide/detail?guideId=2120358988790611711';
  const shared = guide + '&shareSource=copy&from=%E6%A3%AE%E7%A9%BA%E5%B2%9B';
  for (const url of [guide, shared, guide.replace('game.skland.com', 'GAME.SKLAND.COM:443'), videoUrl]) {
    assert.equal(format.lineupSourceHref(url), new URL(url).href);
  }
});

test('阵容来源拒绝冒充森空岛攻略的主机、路径和攻略编号', () => {
  assert.equal(typeof format.lineupSourceHref, 'function');
  const detail = 'https://game.skland.com/arknights/stronghold-guide/detail';
  const guide = detail + '?guideId=2120358988790611711';
  for (const url of [detail, detail + '?guideId=', detail + '?guideId=１２３', detail + '?guideId=1e3',
    detail + '?guideId=%31', detail + '?%67uideId=1', guide + '&guideId=2', guide + '&%67uideId=2',
    guide + '&shareSource=copy paste', guide.replace('https:', 'http:'),
    guide.replace('game.skland.com', 'game.skland.com:444'), guide.replace('game.skland.com', 'game.skland.com.example.test'),
    guide.replace('game.skland.com', 'game.skland.com@evil.test'), guide.replace('game.skland.com', '@game.skland.com'),
    guide.replace('/detail?', '/draft/../detail?'), guide.replace('/detail?', '/%64etail?'), guide.replace('/detail?', '/detail/?')]) {
    assert.equal(format.lineupSourceHref(url), '', url);
  }
});

test('阵容介绍显示标题和三种目录图标，保留代码示例并转义用户文字', () => {
  assert.equal(typeof format.renderDescriptionProse, 'function');
  const catalog = {
    operators: { 地灵: { image: '/portraits/earth.webp' } },
    equipment: { 坚守盾牌: { image: '/gear-icons/shield.png' } },
    strategies: { 集火: { image: '/strategy-icons/fire.png' } }
  };
  const rendered = format.renderDescriptionProse([
    '## 核心循环', '{{op:地灵}} → {{gear:坚守盾牌}} → {{strategy:集火}}',
    '<script> {{gear:目录中缺失}} `{{op:地灵}}`', '~~~text', '# {{op:地灵}}', '~~~'
  ].join('\n'), catalog);
  assert.match(rendered, /<h4>核心循环<\/h4>/);
  assert.match(rendered, /<img [^>]*src="\/portraits\/earth.webp"[^>]*alt="地灵"/);
  assert.match(rendered, /<img [^>]*src="\/gear-icons\/shield.png"[^>]*alt="坚守盾牌"/);
  assert.match(rendered, /<img [^>]*src="\/strategy-icons\/fire.png"[^>]*alt="集火"/);
  assert.match(rendered, /&lt;script&gt; \[目录中缺失\] `\{\{op:地灵\}\}`/);
  assert.match(rendered, /~~~text\n# \{\{op:地灵\}\}\n~~~/);
});
