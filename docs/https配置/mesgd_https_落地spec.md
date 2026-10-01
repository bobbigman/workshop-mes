# mesgd.cn HTTPS 落地规格（给 Cursor 实施）

> 目标：`https://mesgd.cn:11302` 手机/豆包/微信/MCP 可访问，证书可信无告警。
> 链路：`https://mesgd.cn:11302` → 转发机 192.168.2.147:11302（TCP 透传，无需改）→ 本机 Kestrel **8080**（HTTPS 终止）。

## 0. 证书文件（多隆已签发并归档）
```
D:\WorkShop\backend\certs\mesgd\
├─ fullchain.pem   (4796 B)  ← 叶子+链
├─ privkey.pem     (241 B)   ← EC P-256 私钥
├─ cert.pem        (1273 B)
└─ mesgd.pfx       (4235 B)  ← 密码: Mes@2026#Secure
```
有效期：2026-09-23 ~ **2026-12-22**（Let's Encrypt，90 天）。

## 1. Kestrel 绑定 HTTPS（关键：ASPNETCORE_URLS 与证书分开配）
**start.bat 已设 `ASPNETCORE_URLS=http://0.0.0.0:8080`（这个变量会盖过 Kestrel 的 Endpoints 段）。**
所以正确做法是：
- **URL 由 start.bat 的 `ASPNETCORE_URLS` 提供**（改成 https 即可）；
- **证书由 appsettings.json 的 `Kestrel:Certificates:Default` 提供**（ASPNETCORE_URLS 不带证书信息，Kestrel 从这里找默认证书）。

在 `appsettings.json` 增加 `Kestrel` 段（**不要用 Endpoints，会被 ASPNETCORE_URLS 盖掉**）：
```json
"Kestrel": {
  "Certificates": {
    "Default": {
      "Path": "D:\\WorkShop\\backend\\certs\\mesgd\\mesgd.pfx",
      "Password": "Mes@2026#Secure"
    }
  }
}
```
> 密码只放环境变量更稳：设系统环境变量 `Kestrel__Certificates__Default__Password=Mes@2026#Secure`，去掉上面 `Password` 字段。
> 也可用 PEM（免密码）：`"Path": "...fullchain.pem", "KeyPath": "...privkey.pem"`，二选一。

## 2. start.bat 唯一要改的一行
把：
```
set ASPNETCORE_URLS=http://0.0.0.0:8080
```
改为：
```
set ASPNETCORE_URLS=https://0.0.0.0:8080
```
> 其余不动。

## 3. appsettings.json 必改项
| 键 | 现值 | 改为 |
|---|---|---|
| `Instance.PublicBaseUrl` | `http://localhost:8080` | `https://mesgd.cn:11302` |
| `Mcp.AuthBaseUrl` | `https://27888d1c.r16.cpolar.top`（已死，404） | `https://mesgd.cn:11302` |
| `Mcp.AuthLocalBaseUrl` | `http://localhost:8080` | `http://localhost:8080`（保持） |
| `Mcp.DemoSkipAuth` | true | 暂不动 |

## 4. 重启并验证
```
cd D:\WorkShop\backend\publish-win64
stop.bat
start.bat
```
验证（本机及外网）：
```
curl -skI https://mesgd.cn:11302          # 期望 200，无证书告警
curl -skI https://127.0.0.1:8080          # 本机回环
```
`curl -kv https://mesgd.cn:11302` 看证书 CN=mesgd.cn、签发者 Let's Encrypt、无 ERR_CERT 提示。

## 5. 续期（90 天，到期 2026-12-22）
- 证书工具已装在 `C:\le`（certbot 已打 Windows 无软链补丁）。
- 到期前重跑同一条签发命令即可续期（命令见 httpsDebug 记录）；后续可挂计划任务每周自动 `certbot renew`。
- 续期后如 pfx 重新生成，覆盖 `D:\WorkShop\backend\certs\mesgd\` 并重启。

## 6. 注意
- 不碰 IIS/金蝶（本机 80/443）、不碰 skyler nginx（公网 80/443）。
- 数据库连接串、JWT 等其它配置**保持不变**。
- 证书私钥敏感，勿提交仓库；`certs\mesgd\` 建议加入 .gitignore。
