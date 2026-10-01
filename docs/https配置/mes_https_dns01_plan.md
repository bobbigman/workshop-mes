# mes.webok.net HTTPS 落地完整步骤（DNS-01 + 本机 Kestrel）

> 方案定位：让 `https://mes.webok.net:11302` 用 Let's Encrypt 公网可信证书直接服务本机 MES（D:\WorkShop\backend\publish-win64）。
> 绕开公网 80/443 那台 skyler nginx，全程不碰 IIS/金蝶，不碰 skyler 站。

---

## 0. 动手前必须确认的 3 个前提（缺一先停）

- [ ] **P1｜DNS 权限**：`webok.net` 由花生壳(3322.net)托管。请确认您能在花生壳控制台**新增/删除 TXT 记录**，或能拿到 **Oray API**。
  - 有 API → 走自动 DNS-01；
  - 只有控制台 → 走手动 DNS-01（每次续期人工加一次 TXT，90 天一次）。
- [ ] **P2｜转发形态**：确认 `192.168.2.147` 对公网 `11302 → 本机 8080` 是 **TCP 透传**（TLS 终止在本机 Kestrel）。
  - 若是本机 Kestrel 终止 TLS → 本方案成立；
  - 若 147 那层做了 TLS 终止/反代 → 证书应落在 147，方案改为"证书放前端"，另行调整。
- [ ] **P3｜生产配置载体**：确认线上生效的是 `appsettings.WorkshopMesB.json`（还是环境变量覆盖），避免改错文件。

---

## 1. 装证书工具
- 下载 **win-acme**（GitHub 官方发布版，Windows 最顺，本机已有 .NET 8 可直接跑）。
- 解压到 `D:\Tools\win-acme`，运行 `wacs.exe`（交互模式）。

## 2. 用 DNS-01 签发 `mes.webok.net` 证书
1. `wacs.exe` → **Create certificate (simple)**。
2. 输入域名：**`mes.webok.net`**（SAN 只这一个，别多绑）。
3. 验证方式选 **DNS**：有 Oray 插件就选它；没有选 **Manual**（工具会给出要加的 TXT 值）。
4. 按提示把记录加到花生壳控制台：
   - 类型 `TXT`，主机 `_acme-challenge`（完整名 `_acme-challenge.mes.webok.net`），值为工具给出的字符串。
   - 等生效：`Resolve-DnsName _acme-challenge.mes.webok.net -Type TXT` 能看到值后，回 `wacs.exe` 回车继续。
5. 签发成功后，win-acme 默认把证书装入 **Windows 证书库（Cert:\LocalMachine\My）**。
6. 记下证书指纹 Thumbprint，后续用。

## 3. 导出 .pfx 供 Kestrel 使用
- 从证书库导出含私钥的 **.pfx**：
  `D:\WorkShop\backend\publish-win64\certs\mes.pfx`
- 设一个强密码，密码只放**环境变量**（例：环境变量 `MES_CERT_PWD`），不写进任何配置文件/仓库。

## 4. 配置 Kestrel 监听 HTTPS（8080）【Coder 改代码，豆包给规格】
- 目标：Kestrel 在 **8080**（公网 11302 的转发目标端口）提供 HTTPS，证书用上面的 pfx。
- 规格（由 Cursor 实现，改 `Program.cs` / 配置）：
  - Kestrel 监听：`https://0.0.0.0:8080`（外部 11302→8080 透传后即得 `https://mes.webok.net:11302`）
  - 证书来源：`Kestrel:Certificates:Default`：Path = `D:\WorkShop\backend\publish-win64\certs\mes.pfx`，Password = `%MES_CERT_PWD%`
  - 若想保留内网 http，可同时监听 `http://127.0.0.1:8081`（不冲突）。
- 注意：对外 8080 由 http 改 https 后，本地 `http://localhost:8080` 访问会失效，统一走 https。

## 5. 改应用对外地址【Coder 改，豆包给规格】
- `Instance.PublicBaseUrl` → `https://mes.webok.net:11302`
- `Mcp.AuthBaseUrl` → `https://mes.webok.net:11302`（或对应 MCP 接口路径）
- `Mcp.AuthLocalBaseUrl` → `https://127.0.0.1:8080`（本地回环用）
- 写入生产配置（`appsettings.WorkshopMesB.json` 或环境变量），**不要改 dev 的 `appsettings.json`**。

## 6. 重启并验证
- 停：`stop.bat`；启：`start.bat`。
- 验证清单：
  - 浏览器打开 `https://mes.webok.net:11302` → **无证书警告**，能进登录页。
  - `curl -skI https://mes.webok.net:11302` → `Server: Kestrel`，证书 CN=`mes.webok.net`。
  - 手机摄像头扫码报工可用；MCP `/mcp` 可被豆包/微信/扣子读到。
  - 确认没影响 IIS/金蝶：`http://localhost:80` 仍是金蝶，无改动。

## 7. 续期自动化
- Let's Encrypt 证书 90 天有效。用 win-acme 装 **Windows 计划任务**自动续期。
- 关键点：续期后 Kestrel 要**重载新证书**（重启进程，或应用内监听证书文件变化自动重读——由 Cursor 实现为佳）。
- 若走手动 DNS 模式：续期时仍需人工加一次 TXT → 长期用建议争取 Oray API，或考虑（未来）换支持 ACME API 的 DNS。

---

## 红线（全程遵守）
1. **不碰** IIS / 金蝶 K3Cloud、**不碰** skyler nginx。
2. SAN 只绑 `mes.webok.net`。
3. 密钥/证书密码只进环境变量，绝不进仓库。
4. 完成标志：`https://mes.webok.net:11302` 浏览器无证书警告、手机扫码可用、MCP 可读。

## 待确认项（答复后即可开工）
- P1 花生壳能否加 TXT / 有无 Oray API？
- P2 11302→8080 是否 TCP 透传？
- P3 生产配置载体是哪个？
