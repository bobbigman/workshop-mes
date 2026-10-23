# 42-git push 失败（TLS/SSL 层）排查手册

> 配套技能：`git-push-tls-troubleshoot`（豆包用户级技能，跨会话可复用）。
> 沉淀日期：2026-10-23。来源：workshop-mes 仓库推 GitHub 失败的完整复盘。

## 触发场景

`git push` 推不上 GitHub 等远程，报 TLS/SSL 层错误：`unable to access`、`schannel: failed to receive handshake`、`unexpected eof while reading`、`SSL_read`、`TLS connect error`、`SSL certificate problem`，或「开了 Clash/代理还是推不动」。

## 铁律：先取证，再下结论

别一看到 TLS 报错就去改证书、修 schannel 缓存、换代理、怀疑被墙——大多白费。按下面分层取证，每层一条命令钉死一个方向。

## 分层诊断（按顺序）

### 第 1 层：网络到底通不通

```powershell
Test-NetConnection github.com -Port 443   # 看 TcpTestSucceeded
curl.exe -sS -o NUL -w 'code=%{http_code} err=%{errormsg}' --max-time 20 https://github.com
```
- TCP 通 + curl 返回 **200** → 目标没被墙、TLS 能成功 → 直接跳到第 4 层（HTTP 版本），**别再怀疑被墙/网络**。
- TCP 通 + curl 失败 → 才考虑代理/被墙（第 3 层）。

> 关键判断：**`curl`（Windows 原生 schannel）能直连 200，但 `git` 直连握手失败** → 网络和证书都没问题，问题在 git 自己的连接栈，通常就是 HTTP/2。

### 第 2 层：确认 git 有没有被代理/环境变量劫持

```powershell
Get-ChildItem Env:* | Where-Object { $_.Name -match 'PROXY' }
git config --get http.proxy
git config --get http.sslBackend
```
- 环境变量里有 `http_proxy/https_proxy/ALL_PROXY` 指向坏代理 → git 会读它们（git 不读 Windows「系统代理」WinINET，但**读环境变量代理**）。
- 没有环境变量、git 无代理配置 → git 确为直连。

### 第 3 层：代理是否相关（仅当第 1 层 curl 失败才查）

```powershell
# 走代理测目标（Clash 混合端口常见 7897）
curl.exe -x http://127.0.0.1:7897 -sS -o NUL -w '%{http_code}' https://github.com
```
- 走代理通 → 代理 OK；走代理也不通 → 换节点/切全局模式（Clash「连接」页看该域名是 DIRECT 还是 PROXY）。

### 第 4 层：HTTP 版本（最常见根因）

git 默认用 **HTTP/2**（经 libcurl + ALPN），某些 Windows 网络/中间设备会在 TLS 握手时把它重置，报 `unexpected eof while reading` / `schannel: failed to receive handshake`。

```powershell
# 一次性探针（不改配置）：连 GitHub 仓库 HEAD
git -c http.version=HTTP/1.1 ls-remote https://github.com/<user>/<repo>.git HEAD
```
- **通了**（返回 HEAD hash）→ 根因就是 HTTP/2 被重置，解法 = 让 git 走 HTTP/1.1。
- 若仍失败，再叠加 `-c http.sslBackend=openssl` 试。

## 落地修复（真根因是 HTTP/2 时）

持久化（推荐，一次配好）：
```powershell
git config --global http.version HTTP/1.1
```
一次性（不想改配置时）：
```powershell
git -c http.version=HTTP/1.1 push origin master
```
副作用：所有 git 远程走 HTTP/1.1（不用 HTTP/2），功能无碍。
验证：`git status -sb` 不再显示 `ahead`；`git push` 正常。

## 判定表（速查）

| 报错 / 现象 | 指向 | 处理 |
|---|---|---|
| curl 直连 200，git 直连 `unexpected eof` | **git 的 HTTP/2 被重置**（最常见） | `git config --global http.version HTTP/1.1` |
| curl 直连失败（000 / 35 / 56） | 代理/被墙 | 走代理测试、切节点/全局 |
| 环境变量有坏 `http_proxy` | git 被环境变量劫持 | 清掉/修正该变量 |
| schannel `AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS` | 沙箱/受限环境读不到凭据存储 | 不是机器问题，别修证书 |
| schannel `failed to receive handshake` | 已走到握手才被重置 | 优先查 HTTP/2，其次代理 |

## 避坑（别再踩）

- **不要**一上来就修 schannel 缓存 / 换根证书——多数时候与证书无关。
- **不要**过早下「被墙」结论——先跑 `curl` 直连，200 就排除了。
- **不要**误以为 Clash 开了「系统代理」git 就会走——git 不读 WinINET，只读环境变量代理或显式 `http.proxy`。
- 沙箱/容器里的 `SEC_E_NO_CREDENTIALS` 是环境限制，不代表真实机器有问题。

## 真实案例（2026-10-23, workshop-mes）

- 现象：`git push` 到 `github.com/bobbigman/workshop-mes.git` 报 `schannel: failed to receive handshake` / `openssl: unexpected eof, errno 10004`，开 Clash(7897) 也一样。
- 排查：`curl` 直连 github **200** → 排除被墙；环境变量无 `http_proxy` → 排除代理劫持；`git -c http.version=HTTP/1.1 ls-remote` **通** → 锁定 HTTP/2 被重置。
- 修复：`git -c http.version=HTTP/1.1 push origin master` 一次成功；建议持久 `git config --global http.version HTTP/1.1`。
