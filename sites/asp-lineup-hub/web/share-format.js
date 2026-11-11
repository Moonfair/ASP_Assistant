const LINEUP_LIMIT = 10;
const DESCRIPTION_LIMIT = 12_000;
const SHARE_LIMIT = 100_000;
const SHARE_COMMENT = '# ASP Assistant · 明日方舟「卫戍协议」实时辅助工具 · 官网 https://aspa.moonfair.cn';

function validLineupName(name, limit) {
  return typeof name === 'string' && name.trim().length > 0 && name.length <= limit
    && !/[\x00-\x1f\x7f-\x9f]/.test(name);
}

function validateLineup(content) {
  if (!content || !validLineupName(content.Name, 80) || !validLineupName(content.Author, 80)) {
    throw new Error('阵容名称和作者需各填写 1–80 字。');
  }
  if (content.Description != null && (typeof content.Description !== 'string' || content.Description.length > DESCRIPTION_LIMIT)) {
    throw new Error('阵容介绍最多支持 12,000 字。');
  }
  const strategies = content.AvailableStrategies;
  if (strategies != null && (!Array.isArray(strategies) || strategies.length > 100
    || strategies.some(name => !validLineupName(name, 100)) || new Set(strategies).size !== strategies.length)) {
    throw new Error('可用策略需为有效且不重复的名称，最多 100 项。');
  }
  if (!Array.isArray(content.Members) || content.Members.length < 1 || content.Members.length > LINEUP_LIMIT) {
    throw new Error('每个阵容需要 1–10 名干员。');
  }
  const names = new Set();
  for (const member of content.Members) {
    if (!member || !validLineupName(member.OperatorName, 100) || names.has(member.OperatorName)) {
      throw new Error('干员名称必须有效，且同一干员只能加入一次。');
    }
    names.add(member.OperatorName);
    if (typeof member.IsStackingCore !== 'boolean' || typeof member.IsDamageCore !== 'boolean') {
      throw new Error('干员的核心标记无效。');
    }
    for (const item of [member.Equipment1, member.Equipment2]) {
      if (item !== '' && !validLineupName(item, 100)) throw new Error('装备名称无效。');
    }
  }
}

function validateLineupCatalog(content, operators, equipment) {
  for (const member of content.Members) {
    if (!Object.hasOwn(operators, member.OperatorName)) {
      throw new Error(`当前目录没有干员“${member.OperatorName}”，请检查赛季或更新数据。`);
    }
    for (const item of [member.Equipment1, member.Equipment2]) {
      if (item && !Object.hasOwn(equipment, item)) {
        throw new Error(`当前目录没有装备“${item}”，请检查赛季或更新数据。`);
      }
    }
  }
}

function parseLineupShare(value) {
  if (typeof value !== 'string' || value.length > SHARE_LIMIT) throw new Error('分享文本需在 100,000 字以内。');
  const share = value.trim();
  const newline = share.indexOf('\n');
  const header = share.slice(0, newline).replace(/\r$/, '');
  if (newline < 0 || !/^ASPA-LINEUP\/[12]$/.test(header)) {
    throw new Error('请使用 ASP Assistant 导出的 ASPA-LINEUP/1 或 /2 分享文本。');
  }
  const commentStart = newline + 1;
  const commentEnd = share.startsWith('# ', commentStart) ? share.indexOf('\n', commentStart) : newline;
  const payloadStart = commentEnd < 0 ? share.length : commentEnd + 1;
  let data;
  try { data = JSON.parse(share.slice(payloadStart)); }
  catch { throw new Error('分享文本不完整，无法读取阵容。'); }
  const content = {
    Name: data?.Name, Author: data?.Author,
    Members: Array.isArray(data?.Members) ? data.Members.map(member => {
      const { OperatorName, IsStackingCore = false, IsDamageCore = false, Equipment1 = '', Equipment2 = '' } = member ?? {};
      return { OperatorName, IsStackingCore, IsDamageCore, Equipment1, Equipment2 };
    }) : null,
    ...(data?.Description != null ? { Description: data.Description } : {}),
    ...(data?.AvailableStrategies != null ? { AvailableStrategies: data.AvailableStrategies } : {})
  };
  validateLineup(content);
  const exact = Object.keys(data).every(key => ['Name', 'Author', 'Members', 'Description', 'AvailableStrategies'].includes(key))
    && data.Members.every(member => Object.keys(member).every(key =>
      ['OperatorName', 'IsStackingCore', 'IsDamageCore', 'Equipment1', 'Equipment2'].includes(key)));
  return {
    content,
    share: exact ? share : header + '\n' + share.slice(commentStart, payloadStart) + JSON.stringify(content)
  };
}

function serializeLineupShare(content) {
  validateLineup(content);
  const { Name, Author, Members, Description, AvailableStrategies } = content;
  const hasStrategies = AvailableStrategies?.length > 0;
  return 'ASPA-LINEUP/' + (Description || hasStrategies ? '2' : '1') + '\n' + SHARE_COMMENT + '\n'
    + JSON.stringify({ Name, Author, Members, ...(Description ? { Description } : {}),
      ...(hasStrategies ? { AvailableStrategies } : {}) }, null, 2);
}
