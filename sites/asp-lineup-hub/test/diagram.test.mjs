import assert from 'node:assert/strict';
import test from 'node:test';
import { findDiagramBlocks, parseDiagram, formatDiagram, arrangeDiagram } from '../web/diagram-format.js';

test('侧边栏流程图往返保留节点、连线、高亮、注释、坐标和换行', () => {
  const lines = ['flowchart LR', '%% 备注', 'A[{{op:地灵}}]', 'B[{{gear:坚守盾牌}}]', 'A -->|装备| B',
    'highlight A', 'note A: 核心', '%% aspa-layout A 211 88', '%% aspa-layout B 419 132'];
  const diagram = ['```mermaid', ...lines, '```'].join('\r\n');
  const description = '打法\r\n```text\r\n示例\r\n```\r\n' + diagram + '\r\n补充说明';
  const { blocks, unclosed } = findDiagramBlocks(description);
  assert.equal(unclosed, false);
  assert.equal(blocks.length, 1);
  const [block] = blocks;
  assert.equal(description.slice(block.start, block.end), diagram);
  const graph = parseDiagram(block.lines);
  assert.equal(graph.horizontal, true);
  assert.deepEqual(graph.edges, [{ from: 'A', to: 'B', label: '装备' }]);
  assert.deepEqual(arrangeDiagram(graph).get('A'), { x: 211, y: 88 });
  assert.deepEqual(arrangeDiagram(graph).get('B'), { x: 419, y: 132 });
  assert.equal(formatDiagram(graph, block.kind, '\r\n'), diagram);
});

test('旧布局注释按侧边栏规则忽略，引用和坐标不合法时报错', () => {
  const node = 'A[{{op:地灵}}]';
  assert.deepEqual(arrangeDiagram(parseDiagram([node, '%% asp-pos A 255 98'])).get('A'), { x: 18, y: 18 });
  assert.throws(() => parseDiagram([node, 'A --> B']), /未定义/);
  assert.throws(() => parseDiagram([node, '%% aspa-layout A 16001 0']), /坐标/);
  assert.throws(() => parseDiagram([node, node]), /重复/);
});
