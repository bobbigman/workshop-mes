# 车间 MES 系统 HTTPS 上线完整操作手册

> 目标：让 `http://103.39.222.133:11302`（裸 IP）升级为公网可信 `https://mesgd.cn:11302`。
> 适合：手机扫码、豆包/微信/扣子 MCP 调 `/mcp` 都需要可信证书的场景。
> 记录人：多隆老同学　·　日期：2026-09-23

---

## 0. 为什么必须买新域名（前置判断）

| 问题 | 结论 |
|---|---|
| 裸 IP（103.39.222.133）能签公网证书吗？ | **不能**，Let's Encrypt 铁律：只给域名签 |
| 原域名 webok.net 能签吗？ | 域名控制权在花生壳/公云，用户无账号，且公云 API 不能加 TXT → **堵死** |
| HTTP-01 方案能走吗？ | 公网 80 被 skyler 个人 nginx 占用，无 .well-known 转发 → **堵死** |
| 结论 | **只有"买一个自己掌控的新域名"一条路** |

**决策：** 买 `mesgd.cn`（腾讯云 DNSPod，首年 ¥33，续费 ¥39/年，比 .site 的 ¥122 划算）。

---

## 第 1 步：注册域名

1. 用 **微信/QQ 登录腾讯云**（网址 `cloud.tencent.com`）。
2. 搜索并购买域名 **`mesgd.cn`**（.cn 首年约 ¥33）。
3. 按提示完成 **实名认证**（个人身份证/企业，审核约几分钟到 1~3 周，域名先可用，备案可后台慢慢跑）。
4. 付款成功，域名即归您所有。

> 注意：不要在腾讯云另买"SSL 证书 1 元/68 元"那类商品——本方案用 **Let's Encrypt 免费证书**，不花这笔钱。

---

## 第 2 步：DNSPod 添加 A 记录

1. 打开 DNSPod 控制台 `console.dnspod.cn`（用腾讯云账号登录）。
2. 左侧菜单 **「我的域名」** → 找到 **`mesgd.cn`** → 点右侧 **「解析」**。
3. 点 **「添加记录」**，按下面填：
   | 字段 | 值 |
   |---|---|
   | 主机记录 | `@` |
   | 记录类型 | **A** |
   | 记录值 | **`103.39.222.133`** |
   | 权重 | `100`（单条记录无所谓） |
   | TTL | `600` |
4. 点 **「确认」** 保存。

**验证**（几分钟后）：
```
nslookup mesgd.cn
```
应返回 `103.39.222.133`。

---

## 第 3 步：创建腾讯云 API 密钥（DNS-01 签证书用）

1. 腾讯云控制台 → 右上角头像 → **「访问管理 CAM」→「API 密钥管理」**。
2. 点 **「新建密钥」**，得到：
   - **SecretId**（形如 `AKID...`）
   - **SecretKey**（一串随机字符）
3. 立刻复制保存（**只显示一次，关了就看不到**）。

> 这是腾讯云 API 密钥对（不是 DNSPod 的 ID.Token）。用于自动加 TXT 验证记录。
> ⚠️ 属敏感信息，勿提交代码仓库。

---

## 第 4 步：服务器环境核查

在要部署的服务器上（跑 MES 的那台）确认：
```
hostname                 # 确认是对的那台机器
Get-NetIPAddress         # 看内网 IP
Get-Process WorkshopMes  # MES 进程在跑
netstat -ano | findstr :8080   # 监听 0.0.0.0:8080
```
本机情况（参考）：
- git / openssl / acme.sh / win-acme：**都没有**
- python 3.14 + pip：**有**
- 能通 Let's Encrypt（`acme-v02.api.letsencrypt.org`）、能通 pypi；**github 不通**

> 结论：因为 github 不通，用 **pip 短路径装 certbot + 腾讯云 DNS 插件** 这条路最稳。

---

## 第 5 步：安装证书工具（certbot + 腾讯 DNS 插件）

> 坑：直接 `pip install certbot` 会因 **Windows 长路径限制**（沙箱 site-packages 路径太深）安装失败。
> 解法：装到**短路径** `C:\le`。

```powershell
New-Item -ItemType Directory -Force -Path "C:\le"
python -m pip install --target C:\le certbot certbot-dns-tencentcloud
```

装好后：
- 命令：`C:\le\bin\certbot.exe`
- 运行前设环境变量：`$env:PYTHONPATH='C:\le'`
- 腾讯云凭据走环境变量：
  ```powershell
  $env:TENCENTCLOUD_SECRET_ID='<你的SecretId>'
  $env:TENCENTCLOUD_SECRET_KEY='<你的SecretKey>'
  ```

---

## 第 6 步：给 certbot 打 Windows 补丁（关键，绕开符号链接）

**现象：** certbot 在 Windows 上把证书写进 `live` 目录时用 `os.symlink` 建软链，而本机**没开开发者模式/管理员权限，根本建不了符号链接**，报 `FileNotFoundError` / `OSError(22)`。

**补丁（修改 `C:\le\certbot\_internal\storage.py`，共 3 处）：**

1. **`new_lineage`（约 1068 行）**——改为"直接写 archive 再复制到 live，不建软链"：
   ```python
   # 把原来的 os.symlink 循环 + 写入 target 改为：
   with open(archive_target["cert"], "wb") as f_b: f_b.write(cert)
   with util.safe_open(archive_target["privkey"], "wb", chmod=BASE_PRIVKEY_MODE) as f_a: f_a.write(privkey)
   with open(archive_target["chain"], "wb") as f_b: f_b.write(chain)
   with open(archive_target["fullchain"], "wb") as f_b: f_b.write(cert + chain)
   import shutil
   for kind in ALL_FOUR:
       shutil.copy2(archive_target[kind], target[kind])
   ```

2. **`_check_symlinks`（约 593 行）**——真文件也认：
   ```python
   if not os.path.lexists(link):
       raise ...
   if os.path.islink(link):
       target = get_link_target(link)
       if not os.path.exists(target): raise ...
   ```

3. **`get_link_target`（约 239 行）**——真文件返回自身：
   ```python
   except OSError:
       if os.path.lexists(link) and not os.path.islink(link):
           return os.path.abspath(link)
       raise errors.CertStorageError(...)
   ```

> 若某次运行中途失败留下 `live\...\cert.pem` 断软链，`Remove-Item -Recurse` 会卡死，改用：
> ```powershell
> cmd /c del /f /q "C:\le\config\live\mesgd.cn\cert.pem"
> cmd /c rmdir /s /q "C:\le\config\archive\mesgd.cn"
> ```

---

## 第 7 步：签发证书（DNS-01）

```powershell
$env:PYTHONPATH='C:\le'
$env:TENCENTCLOUD_SECRET_ID='<SecretId>'
$env:TENCENTCLOUD_SECRET_KEY='<SecretKey>'

& "C:\le\bin\certbot.exe" certonly --authenticator dns-tencentcloud `
    --dns-tencentcloud-propagation-seconds 30 `
    -d mesgd.cn --non-interactive --agree-tos -m admin@mesgd.cn `
    --config-dir C:\le\config --work-dir C:\le\work --logs-dir C:\le\logs
```

**过程：** 自动注册 ACME 账户 → 创建订单 → 调腾讯云 DNS API 加 `_acme-challenge.mesgd.cn` TXT → 等 30 秒传播 → 校验 → 出证 → 删除 TXT。

**成功标志：** 输出含 NEXT STEPS / renew 提示，`C:\le\config\live\mesgd.cn\` 出现 4 个文件：
```
cert.pem  /  chain.pem  /  fullchain.pem  /  privkey.pem
```

**验证证书：**
```
证书 CN=mesgd.cn, SAN=mesgd.cn
签发：Let's Encrypt，有效期 90 天
```

---

## 第 8 步：归档证书 + 生成 pfx

1. 建目录并拷入：
   ```powershell
   $dest = "D:\WorkShop\backend\certs\mesgd"
   New-Item -ItemType Directory -Force -Path $dest
   Copy-Item "C:\le\config\live\mesgd.cn\fullchain.pem" "$dest\fullchain.pem"
   Copy-Item "C:\le\config\live\mesgd.cn\privkey.pem"   "$dest\privkey.pem"
   Copy-Item "C:\le\config\live\mesgd.cn\cert.pem"      "$dest\cert.pem"
   ```

2. 生成 pfx（可选，用 python cryptography）：
   ```python
   from cryptography import x509
   from cryptography.hazmat.primitives import serialization
   from cryptography.hazmat.primitives.serialization import pkcs12
   cert = x509.load_pem_x509_certificate(open(r'D:\WorkShop\backend\certs\mesgd\cert.pem','rb').read())
   key  = serialization.load_pem_private_key(open(r'D:\WorkShop\backend\certs\mesgd\privkey.pem','rb').read(), password=None)
   p12  = pkcs12.serialize_key_and_certificates(b'mesgd', key, cert, None,
              serialization.BestAvailableEncryption(b'Mes@2026#Secure'))
   open(r'D:\WorkShop\backend\certs\mesgd\mesgd.pfx','wb').write(p12)
   ```

归档后目录：
```
D:\WorkShop\backend\certs\mesgd\
├─ cert.pem / chain.pem / fullchain.pem / privkey.pem / mesgd.pfx
```

---

## 第 9 步：配置 Kestrel 启用 HTTPS

**文件 1：`D:\WorkShop\backend\publish-win64\appsettings.json`**

① 增加 `Kestrel` 段（用 PEM 免密码；**不要用 Endpoints，会被 start.bat 的 ASPNETCORE_URLS 盖掉**）：
```json
"Kestrel": {
  "Certificates": {
    "Default": {
      "Path": "D:\\WorkShop\\backend\\certs\\mesgd\\fullchain.pem",
      "KeyPath": "D:\\WorkShop\\backend\\certs\\mesgd\\privkey.pem"
    }
  }
}
```

② 改 `Instance.PublicBaseUrl`：
```json
"PublicBaseUrl": "https://mesgd.cn:11302"
```

③ 改 `Mcp.AuthBaseUrl`（原 cpolar 已失效）：
```json
"AuthBaseUrl": "https://mesgd.cn:11302"
```

**文件 2：`D:\WorkShop\backend\publish-win64\start.bat`**

把：
```
set ASPNETCORE_URLS=http://0.0.0.0:8080
```
改为：
```
set ASPNETCORE_URLS=https://0.0.0.0:8080
```

**校验 JSON：**
```powershell
python -c "import json; json.load(open(r'D:\WorkShop\backend\publish-win64\appsettings.json',encoding='utf-8')); print('OK')"
```

---

## 第 10 步：重启并验证

```powershell
cd D:\WorkShop\backend\publish-win64
stop.bat      # 停旧进程（旧配置是 http）
start.bat     # 起新进程（https://0.0.0.0:8080）
```

**验证（本机）：**
```
curl -skI https://127.0.0.1:8080          # 期望 200
curl -kv  https://127.0.0.1:8080          # 看证书 CN=mesgd.cn、签发者 Let's Encrypt、无告警
```

**验证（外网，经 11302 透传）：**
```
curl -skI https://mesgd.cn:11302          # 期望 200，无证书告警
```
手机浏览器打开 `https://mesgd.cn:11302` 应无"不安全"提示。

> 链路：`https://mesgd.cn:11302` → 转发机 192.168.2.147:11302（TCP 透传，无需改）→ 本机 Kestrel 8080（HTTPS 终止）。

---

## 第 11 步：续期（很重要，90 天一次）

- 本次证书有效期：**2026-09-23 ~ 2026-12-22**。
- 到期前重跑**第 7 步的签发命令**即可续期（certbot 已打补丁，能落盘）。
- 建议挂**计划任务**每 60 天自动跑一次：
  ```
  schtasks /create /tn "MES_CertRenew" /tr "C:\le\bin\certbot.exe renew ..." /sc weekly
  ```
- 续期后若用 pfx，需重新生成并覆盖 `certs\mesgd\`，然后重启 MES。

---

## 注意事项（铁律）

1. **不碰** 本机 IIS/金蝶（占 80/443）、**不碰** 公网 skyler nginx（占 80/443）。
2. **数据库连接串、JWT 等其余配置一律不动**。
3. 证书私钥、pfx、SecretKey 属**敏感信息**，勿提交仓库；`certs\mesgd\` 建议加 `.gitignore`。
4. 换客户服务器部署时：证书**跟域名走不跟机器走**，只要 mesgd.cn A 记录指向目标机公网 IP，同一套证书文件直接拷过去 + 第 9~10 步配置即可；若客户要独立域名，则另签（同一套工具链）。

---

## 关键信息速查表

| 项 | 值 |
|---|---|
| 域名 | `mesgd.cn` |
| 目标 HTTPS | `https://mesgd.cn:11302` |
| 证书签发方 | Let's Encrypt（免费） |
| 证书有效期 | 2026-09-23 ~ 2026-12-22（90 天） |
| 证书文件 | `D:\WorkShop\backend\certs\mesgd\` |
| 证书工具 | `C:\le\bin\certbot.exe`（已打 Windows 补丁） |
| Kestrel 监听 | `https://0.0.0.0:8080` |
| 外部链路 | mesgd.cn:11302 → 192.168.2.147:11302 → 本机 8080 |
| pfx 密码 | `Mes@2026#Secure`（敏感，勿外传） |
