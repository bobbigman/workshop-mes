# 排查指令 278：git push 到 GitHub 失败（TLS 握手被打断）——交 DeepSeek 查根因

> 性质：网络/工具排查（非开发）。
> 一句话目标：判断 git push 到 GitHub 的 TLS 握手为何总被打断，给出根因 + 可验证命令 + 落地修复，让 `git push origin master` 能推上去。
> 来源：黑帽大人在「手机 H5 对齐电脑端」开发收尾时推不动，开了 Clash Verge 代理仍失败。

## 现象

Windows 上 `git push origin master` 推送 `https://github.com/bobbigman/workshop-mes.git` 失败，报 TLS 层错误，推不上去。本地已领先 origin 4 个提交待推送。

## 环境

- Windows；Git for Windows **2.51.2.windows.1**（默认 TLS 后端 = schannel）。
- 本机装了 **Clash Verge** 代理；混合端口 `127.0.0.1:7897` 已确认监听（OPEN）。
- git 仓库与全局均**未配置** `http.*`（无代理、无 `sslBackend` 覆盖）→ 当前 git 实际走默认 schannel 直连。

## 已做的真实诊断（证据，勿当猜测）

| # | 动作 | 结果 |
|---|---|---|
| 1 | `Test-NetConnection github.com -Port 443` | `TcpTestSucceeded=True`（TCP 通） |
| 2 | 直连 + 默认 schannel：`git push origin master` | `fatal: unable to access ... schannel: failed to receive handshake, SSL/TLS connection failed` |
| 3 | 直连 + OpenSSL：`git -c http.sslBackend=openssl push origin master` | `TLS connect error: 0A000126:SSL routines::unexpected eof while reading` |
| 4 | 端口探测 127.0.0.1 | 仅 **7897 OPEN**；7890/7891/7892/7893/7898 全 closed |
| 5 | 代理 + schannel：`git -c http.proxy=http://127.0.0.1:7897 push origin master` | 仍 `schannel: failed to receive handshake` |
| 6 | 代理 + OpenSSL：`git -c http.proxy=http://127.0.0.1:7897 -c http.sslBackend=openssl push origin master` | `OpenSSL SSL_read: unexpected eof while reading, errno 10004` |

要点：TCP 能连上 443，但一进入 TLS 握手就被重置；schannel 与 OpenSSL 两种后端、直连与走代理四种组合全失败。

## 请 DeepSeek 做

1. 判断最可能的根因，在以下方向中选并给依据：
   a. git 的 schannel TLS 后端与系统/代理的兼容问题；
   b. Clash Verge 当前**节点**到 GitHub 实际不通或被重置（节点失效/被墙）；
   c. Clash **规则模式**下 GitHub 走了直连被墙，流量没真正进代理（虽有代理端口，但该域名没走代理）；
   d. 其它（如本机安全软件/防火墙/根证书、系统 schannel 缓存损坏等）。
2. 给一组可直接执行的排查/验证命令，并说明每步的预期结果如何证明/排除某个根因。建议方向（可扩展）：
   - `curl.exe -x http://127.0.0.1:7897 -I https://github.com`（验证走代理能否通、Clash 是否让该域名走代理）；
   - 查 git 当前 `sslBackend` 与代理生效情况：`git config --get http.sslBackend`、`git config --get http.proxy`；
   - Clash Verge 切「全局」vs「规则」模式、换节点分别再 `git push` 或 `curl` 验证；
   - 直连 `curl.exe -I https://github.com`（确认直连是否也被 TLS 重置）；
   - 判断是否需要持久设置 `git config --global http.sslBackend openssl` 或 `http.proxy http://127.0.0.1:7897`，以及副作用。
3. 给出最终能让 push 成功的落地修复方案，说明副作用与验证方法。

## 验收（≤5 条）

1. 给出 1 条最可能根因，且与上面的证据表不矛盾；
2. 给出一组能直接复现验证的命令（含每步看什么结论）；
3. 给出 1 个落地修复方案，说明副作用与验证；
4. 修复后 `git push origin master` 能推上去（4 个提交）。

## 交付终点

一段可读的排查结论 + 一组验证命令 + 一个修复方案；由黑帽大人转交 DeepSeek 后按结论落地，落成后重试 push。

---

附：待推送提交（本地 ahead origin 4）：
`8d4b510` 手机H5对齐电脑端（工作台/管理操作入口调整 + AI操作帮助角色化）
`42156d4` fix: AI操作帮助去重，去掉消息区重复的问题按钮
`4fb66d6`（编号以 git log 实际为准）fix: AI操作帮助问题面板默认显示3条并支持查看全部10条

---

## 实测结论（2026-10-23，豆包在真实机复核）

**DeepSeek 的"被墙/流量没走代理"推断被证伪，真根因 = git 默认 HTTP/2 连接被本机网络重置，切 HTTP/1.1 即解决。**

关键实测证据（真实机）：
| 动作 | 结果 |
|---|---|
| `curl.exe -sS -o NUL -w %{http_code} https://github.com`（直连） | **200**（github 可达、TLS 成功，未被墙） |
| 环境变量 `http_proxy/https_proxy/ALL_PROXY` | 均无（git 非代理劫持，确为直连） |
| `git -c http.version=HTTP/1.1 -c http.sslBackend=openssl ls-remote ...HEAD` | **通**（返回 HEAD hash） |
| `git -c http.version=HTTP/1.1 ls-remote ...HEAD`（仅 HTTP/1.1，默认 schannel） | **通** → 关键变量是 HTTP/1.1，无需改 sslBackend |
| `git -c http.version=HTTP/1.1 -c http.sslBackend=openssl push origin master` | **成功** `b0ea01d..bebf571` |

结论：
1. 与 Clash 模式/节点/被墙无关，代理可不开；
2. 根因是 git 默认以 HTTP/2（ALPN）连 GitHub 时 TLS 握手被本机网络/中间设备重置（`unexpected eof while reading`）；
3. 解法：git 走 HTTP/1.1。

落地（推荐持久化，避免每次带参数）：
```
git config --global http.version HTTP/1.1
```
副作用：所有 git 远程走 HTTP/1.1（不用 HTTP/2），功能无碍。
验证：`git push origin master` 正常、`git status -sb` 不再显示 ahead。
