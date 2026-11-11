function refreshAccountButton() {
  $('#account-button').innerHTML = icon('people') + (state.user ? '我的账号' : '登录 / 注册');
}

function showAccount() {
  if (!state.user) { showAuth(); return; }
  activeDetail = null;
  modalVersion++;
  setDetailUrl(null);
  modalShell('我的账号', `<p class="intro">当前登录邮箱</p>
    <p class="account-email">${escape(state.user.email)}</p>
    <p class="intro">你的邮箱仅用于登录，不会在阵容、评论或评分中显示。</p>
    <p class="form-error" id="account-error" role="alert" hidden></p>
    <button type="button" class="button secondary" id="logout">退出登录</button>`, 'ACCOUNT');
  $('#logout').addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await api('auth/logout', {});
      state.user = null;
      refreshAccountButton();
      if (button.isConnected) modal.close();
      showToast('已退出登录');
    } catch (failure) {
      if (!button.isConnected) return;
      const error = $('#account-error');
      error.textContent = failure.message;
      error.hidden = false;
      button.disabled = false;
    }
  });
}

function showAuth(next, back) {
  modalVersion++;
  const backButton = back ? '<button type="button" class="button secondary" id="auth-back">返回阵容</button>' : '';
  const bindBack = () => $('#auth-back')?.addEventListener('click', back);

  function enterEmail(email = '') {
    modalShell('邮箱登录 / 注册', `
      <p class="intro">输入邮箱获取登录验证码。首次验证通过后会自动创建账号。</p>
      <form id="email-form">
        <label for="auth-email">邮箱地址</label>
        <input type="email" id="auth-email" class="field" name="email" required maxlength="254"
          autocomplete="email" placeholder="name@example.com" value="${escape(email)}">
        ${challengeMarkup('auth-image')}
        <p class="form-error" data-error role="alert" hidden></p>
        <div class="action-line"><button type="submit" class="button primary" disabled>发送邮件验证码</button>${backButton}</div>
      </form>`, 'EMAIL ACCESS');
    bindBack();
    const form = $('#email-form');
    const challenge = bindChallenge(form, 'login');
    challenge.reload();
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      challenge.submit.disabled = true;
      challenge.message('');
      const address = form.elements.email.value.trim();
      try {
        const sent = await api('auth/code', { email: address, captcha: challenge.payload() });
        if (form.isConnected) enterCode(address, sent);
      } catch (failure) {
        if (!form.isConnected) return;
        challenge.message(failure.message);
        await challenge.reload();
      }
    });
  }

  function enterCode(email, sent) {
    modalShell('输入邮件验证码', `
      <p class="intro">6 位验证码已发送至 <strong>${escape(email)}</strong>，${sent.expiresIn / 60} 分钟内有效。</p>
      <form id="code-form">
        <label for="email-code">邮件验证码</label>
        <input type="text" id="email-code" name="code" class="field" inputmode="numeric" autocomplete="one-time-code"
          required pattern="[0-9]{6}" maxlength="6" placeholder="输入 6 位数字">
        <p class="form-error" data-error role="alert" hidden></p>
        <div class="action-line"><button type="submit" class="button primary">验证并登录</button>
          <button type="button" class="button secondary" id="change-email">修改邮箱 / 重新发送</button>${backButton}</div>
      </form>`, 'VERIFY EMAIL');
    bindBack();
    $('#change-email').addEventListener('click', () => enterEmail(email));
    const form = $('#code-form');
    const submit = $('[type="submit"]', form);
    form.elements.code.focus();
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const error = $('[data-error]', form);
      submit.disabled = true;
      error.hidden = true;
      try {
        const { user } = await api('auth/verify', { id: sent.id, code: form.elements.code.value });
        state.user = user;
        refreshAccountButton();
        if (!form.isConnected) return;
        showToast('已登录');
        if (next) next();
        else modal.close();
      } catch (failure) {
        error.textContent = failure.message;
        error.hidden = false;
        submit.disabled = false;
      }
    });
  }

  if (state.mailAvailable === false) {
    modalShell('邮箱登录 / 注册', '<p class="intro">邮件验证码暂不可用，请稍后再试。</p>' + backButton, 'EMAIL ACCESS');
    bindBack();
  } else enterEmail();
}

function handleExpiredSession(error, next) {
  if (error.code !== 'login_required') return false;
  state.user = null;
  refreshAccountButton();
  const back = activeDetail ? () => showDetail(activeDetail) : null;
  showAuth(next, back);
  return true;
}
