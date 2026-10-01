# 58 - 执行指令 - 远程服务器 HTTPS 证书上线（手机扫码 + MCP）

> 目标：把 
>
> `mes.webok.net`
>
> （当前 HTTP:11302）升级为 HTTPS—— 手机摄像头扫码、豆包 / 扣子 / 方舟 MCP、扫码报工链接全走 HTTPS。
> 状态：待执行（2026-09-23 勘察；
>
> **已确认服务器为 Windows + Windows 版 nginx，主方案 = nginx 反代 + win-acme HTTP-01**
>
> ）。



***

## 一、现状（已核实，含实探）



| 项          | 事实                                                                                                                                                      |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 服务器系统      | **Windows**（远程桌面 `mes.webok.net:11301`，桌面有 "车间管理系统" 图标）                                                                                                 |
| MES 服务     | `mes.webok.net:11302` → `HTTP 200`，`Server: Kestrel` —— .NET 8 自宿主，一个进程扛 静态页 + `/api` + `/mcp`                                                          |
| 服务器 80/443 | **nginx/1.26.2**（Windows 版）托管 `skyler.net.cn` 个人网站；80 对 [mes.webok.net](https://mes.webok.net) 返回 `301 → https://mes.webok.net:443/`                    |
| 443 证书     | `CN=skyler.net.cn`，**DigiCert（Encryption Everywhere DV TLS CA-G2）**，SAN 仅 `skyler.net.cn`、`www.skyler.net.cn`，**不含&#x20;**`mes.webok.net`，2027-02-01 到期 |
| 结论         | `https://mes.webok.net/` 能连上但**证书不匹配，浏览器必有警告**，不能直接复用                                                                                                   |
| 80 可达      | **通**（nginx 80 监听并 301）→ 满足 Let's Encrypt HTTP-01 验证                                                                                                    |
| 域名         | `mes.webok.net` → 公云 3322 DDNS，A 记录指向这台服务器                                                                                                              |
| 公云 3322    | API 无 TXT、产品无 TXT 能力 → **不支持 DNS-01，别走 3322**                                                                                                           |
| 本机 DNS 探测  | 本机 Clash TUN 返回假 IP（198.18.0.28）→ 验证必须在服务器 / 外部设备做                                                                                                      |
| 已有 HTTPS   | 仅开发机前端 mkcert（`frontend/certs/`），自签不可用于公网 / 云端 MCP                                                                                                      |



***

## 二、关键决策（先看）

**服务器上现成 nginx 是天然反向代理 → 最优方案 = nginx 终结 TLS + 反代到内网 Kestrel。**



| 路径                              | 成本       | 说明                                           | 结论   |
| ------------------------------- | -------- | -------------------------------------------- | ---- |
| **nginx 反代 + win-acme HTTP-01** | 0 元，自动续  | 80 通 → HTTP-01；nginx 443 反代 11302；Kestrel 不动 | ✅ 首选 |
| 迁 dnspod + 自动 DNS-01            | 0 元      | 若无法操作 nginx / 80 不可用时的备选                     | 备选   |
| 商业证书 / 备案                       | 花钱 / 周期长 | 非首选                                          | 不用   |

> 部署形态：
>
> **nginx 监听 80/443，**
>
> `server_name mes.webok.net`
>
> **，443 反代&#x20;**
>
> `http://127.0.0.1:11302`
>
> 。
> 手机、微信、MCP、二维码全走 
>
> `https://mes.webok.net`
>
> （443），端口 11302 对外不再直接暴露。



***

## 三、第 0 步：动手前必核实清单（Windows 服务器，缺一不可）



* [ ] **nginx 装在哪**：`nginx.exe` 目录（如 `C:\nginx`）、主配置 `conf\nginx.conf` 及其 `include` 了哪些 conf（确认往哪个文件加 server 块）。

* [ ] **现有 server 块结构**：谁是默认 server（`listen 80/443 default_server`？）、skyler 站的 `server_name` 是什么 → 搞清 [mes.webok.net](https://mes.webok.net) 现在为何返回 skyler 页（落到默认 / 兜底块）。加**精确&#x20;**`server_name mes.webok.net` 不会抢 skyler（只要 skyler 有自己的 server\_name 或保留 default）。

* [ ] **现有 443 证书怎么配**：当前 `ssl_certificate` / `ssl_certificate_key` 指向哪（照它的路径规范写新的）。

* [ ] **有没有 win-acme /certbot/openssl**：Windows 上首选 **win-acme**（HTTP-01 webroot）。

* [ ] **11302 监听方式**：Kestrel 是 `0.0.0.0` 还是 `127.0.0.1`（反代走 `127.0.0.1:11302`，确认本机能通）。

* [ ] **验证目录（Windows 路径）**：HTTP-01 的 webroot 用 Windows 绝对路径（如 `C:\nginx\html\letsencrypt`），不是 `/var/www/...`。

* [ ] **nginx 重启方式**：Windows 无 systemctl，用 `nginx -t` 校验 + `nginx.exe -s reload`；改前**备份 conf**。

* [ ] **ForwardedHeaders**：反代后 [ASP.NET](https://ASP.NET) 需正确识别 `X-Forwarded-Proto`；必要时后端开 `ForwardedHeaders` 中间件，否则 HTTPS 判断 / 重定向可能串。



***

## 四、执行步骤 A：nginx 反代 + win-acme HTTP-01（首选，Windows 版）

> 以下路径为
>
> **示例**
>
> （
>
> `C:\nginx`
>
> ），以核实清单为准。

### 1. 备份 + 加 server 块



* 先备份现有 `nginx.conf` 及相关 conf。

* 在 nginx include 目录新增 `mes-webok.conf`（或并入现有 conf）：



```
server {

&#x20;   listen 80;

&#x20;   server\_name mes.webok.net;

&#x20;   location /.well-known/acme-challenge/ { root C:/nginx/html/letsencrypt; }

&#x20;   location / { return 301 https://\$host\$request\_uri; }

}

server {

&#x20;   listen 443 ssl;

&#x20;   server\_name mes.webok.net;

&#x20;   ssl\_certificate     C:/nginx/certs/mes-webok/fullchain.pem;

&#x20;   ssl\_certificate\_key C:/nginx/certs/mes-webok/privkey.pem;

&#x20;   location / {

&#x20;       proxy\_pass http://127.0.0.1:11302;

&#x20;       proxy\_set\_header Host \$host;

&#x20;       proxy\_set\_header X-Real-IP \$remote\_addr;

&#x20;       proxy\_set\_header X-Forwarded-For \$proxy\_add\_x\_forwarded\_for;

&#x20;       proxy\_set\_header X-Forwarded-Proto \$scheme;

&#x20;       proxy\_read\_timeout 120s;

&#x20;   }

}
```



* `nginx -t` 通过后 `nginx.exe -s reload`。

* ⚠️ 若默认 server 占 80/443，去掉冲突的 `default_server`；reload 后**分别验证&#x20;**[skyler.net.cn](https://skyler.net.cn)**&#x20;与&#x20;**[mes.webok.net](https://mes.webok.net) 都正常。

### 2. 签发证书（win-acme，HTTP-01 webroot）



* 服务器装 **win-acme**（`https://www.win-acme.com`）。

* 运行向导：目标域名 `mes.webok.net`，验证方式 **HTTP-01（webroot）**，webroot 填 `C:\nginx\html\letsencrypt`。

* 证书输出到 `C:\nginx\certs\mes-webok\`（fullchain.pem + privkey.pem，供 nginx 引用）。

* 自动续期：win-acme 建**计划任务**，续期成功后触发 `nginx.exe -s reload`（win-acme 支持 post-hook）。

### 3. 同步地址（`backend/appsettings.json`）



* `Instance:PublicBaseUrl` → `https://mes.webok.net`

* `Mcp:AuthBaseUrl` → `https://mes.webok.net`（授权链接用，末尾不带斜杠）

* 检查前端 / 二维码硬编码 `http://mes.webok.net:11302` → 全改 `https://mes.webok.net`。

* 若反代后登录 / 重定向异常，后端启用 ForwardedHeaders（见风险节）。

### 4. 更新对外 MCP 与渠道地址



* 豆包连接器、扣子 MCP JSON `url`、方舟 MCP URL → `https://mes.webok.net/mcp`（token 走 Header，禁止拼 `?token=`）

* 打印的报工二维码 → `https://mes.webok.net/#/h5/report?order=...`

* 手机「添加到主屏幕」/ 书签 → `https://mes.webok.net`。

### 5. 内网端口收紧（可选，稳妥）



* 对外仅开 80/443（nginx）；11302 只监听 `127.0.0.1` 或防火墙只放行本机回环，避免 11302 裸 HTTP 直接暴露公网。



***

## 五、备选：迁 dnspod + 自动 DNS-01（nginx 无法操作 / 80 不可用时）

> dnspod（腾讯云解析）免费、能自助加 TXT、有 API、自带 DDNS，win-acme 有 dnspod 插件可全自动续期。



1. **注册 / 登录 dnspod**（`dnspod.cn`，免费）。

2. **添加域名&#x20;**`webok.net`，到**域名注册商处把 NS 改成 dnspod 给的两个**（形如 `f1g1ns1.dnspod.net` / `f1g1ns2.dnspod.net`）。

* 前提：注册商后台能改 NS；公云 3322 若同时是注册商，需确认放开。

1. **等 NS 生效**：外部设备 `Resolve-DnsName webok.net -Type NS`，看到 dnspod 才算切过来。

2. **重建解析**：dnspod 重新加 `mes` 的 A 记录（指向服务器当前公网 IP）。

3. **DDNS 动态更新**：dnspod 动态解析 API / 官方 DDNS 工具，开机自动把 `mes` 更新到当前 IP（替代原 3322 动态更新）。

4. **自动 DNS-01**：win-acme 选 dnspod 插件 + API Token → 签发 + 自动续期。

5. 证书绑到哪：若在 nginx 上则配到 nginx；若走 Kestrel 自宿主则 `Kestrel:Certificates` + `ASPNETCORE_URLS=https://0.0.0.0:11302`（此时才需要动 Kestrel）。

> 说明：
>
> `mes`
>
>  A 记录指向服务器公网 IP 后，
>
> `mes.webok.net`
>
>  访问行为不变，仅 DNS 托管方由 3322 换成 dnspod。



***

## 六、验收清单（全部过才算完成）



* [ ] `https://mes.webok.net` 浏览器打开，锁标正常、**无证书警告**（电脑 + 手机各测一次）

* [ ] 电脑端 https 下能登录、查工单、看板正常

* [ ] 手机 https 打开报工 H5，摄像头扫码**弹出授权弹窗**并能识别 → 实测提交一单

* [ ] 扫码枪 / PDA 硬件扫码头（不调摄像头）仍可正常报工

* [ ] MCP：豆包 / 扣子能读到工具列表（7 个齐全），`query_work_order` 返回真实数据

* [ ] 微信渠道「查工单」「查报工」正常

* [ ] 授权链接 `start_auth` 走 https 打开正常

* [ ] 页面无混合内容（F12 Console 无 `Mixed Content`，无 http:// 资源）

* [ ] `http://mes.webok.net` 自动 301 到 https，不泄漏明文

* [ ] [skyler.net.cn](https://skyler.net.cn)**&#x20;个人网站仍正常**（反代改动不影响伙伴站点）



***

## 七、注意 / 风险



* **Windows 差异**：无 `systemctl`，用 `nginx -t` + `nginx.exe -s reload`；续期用**计划任务**；路径全用 Windows 绝对路径。

* nginx、skyler 站是伙伴的，**改配置前备份、**`nginx -t`**&#x20;校验、低峰期 reload**，确认 skyler 站不受影响。

* **ForwardedHeaders**：反代后 Kestrel 收到的请求是 HTTP（来自 nginx），若 [ASP.NET](https://ASP.NET) 依赖 `Request.IsHttps` / 生成链接，需 `X-Forwarded-Proto` 被识别；必要时后端启用 `ForwardedHeaders` 中间件。`Instance:PublicBaseUrl` 已显式配 https 可缓解部分。

* 证书与 API 凭据只放服务器，禁止提交仓库（`docs/06-开发规约.md`）。

* 切 HTTPS 后，旧 `http://mes.webok.net:11302` 链接（微信收藏、已打印二维码）**全部失效**，需重新生成。

* 生产务必关闭 `Mcp:DemoSkipAuth`、换强 token（`docs/56` 三 - 4）。

* 验证一律在服务器 / 外部设备做，本机 Clash TUN 返回假 IP 会误判。