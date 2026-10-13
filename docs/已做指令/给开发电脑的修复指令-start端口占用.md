# 修复指令：start.bat 在「后端已在运行」时崩溃刷屏（开发电脑执行）

> 状态：已做（2026-10-12）。源头是 `backend/start.bat`（发布时拷进 `publish-win64`）。
>
> 与原稿的差异：第三节那段中文 `echo` 在本机 cmd（代码页 65001）下会少算 UTF-8 字节，把下一行开头吃掉，提示残缺并报「不是内部或外部命令」。bat 本体保持 ASCII、UTF-8 BOM、CRLF；中文由 PowerShell 从 Base64 打出。占用提示里的地址是 `http://localhost:8080/`，与本仓库模板的 `ASPNETCORE_URLS` 一致。`Program.cs` 在交给 Kestrel 之前发现端口已占用，或捕获启动期 AddressInUse，打一行中文并以退出码 2 结束。横幅「本机打开」跟随 `ASPNETCORE_URLS` 的协议。

---

## 一、现象（现场原始输出，可直接作为排查入口）

在发布目录 `publish-win64\` 里**重复双击 `start.bat`** 时出现：

```
[11:51:48 WRN] WorkshopMes 已启动 Instance=local ... Urls=https://0.0.0.0:8080
[11:51:48 ERR] Hosting failed to start
System.IO.IOException: Failed to bind to address https://0.0.0.0:8080: address already in use.
 ---> Microsoft.AspNetCore.Connections.AddressInUseException: 通常每个套接字地址(协议/网络地址/端口)只允许使用一次。
 ---> System.Net.Sockets.SocketException (10048): 通常每个套接字地址(协议/网络地址/端口)只允许使用一次。
   ...（约 40 行英文堆栈，Kestrel / GenericWebHostService / Host.StartAsync）
[11:51:48 FTL] 应用程序终止
Unhandled exception. System.IO.IOException: Failed to bind to address https://0.0.0.0:8080: address already in use.
   ...（又一遍堆栈）
[FAIL] Exit code=-532462766
Check logs\mes-error-*.log
```

看上去像“系统坏了”，实际上后端从头到尾都是好的——旧实例仍在正常服务。

## 二、根因（两条，缺一不可）

1. **`start.bat` 缺少“端口已被占用”的前置判断**，直接执行 `WorkshopMes.exe`。8080 已被上一个实例占用，Kestrel 绑定失败抛 `IOException`。
2. **程序未捕获启动期绑定异常**，成为未处理异常直接崩出进程，退出码 `-532462766`，即 `0xE0434352`（CLR 未处理异常）。只是“重复启动”，却输出一整屏英文堆栈（`Failed to bind ... address already in use`）。

判定基线（避免误判为服务故障）：这是**端口冲突**，不是后端故障。真正的服务异常要看 `logs\mes-error-*.log`。

## 三、要做的改动

### A. 脚本层：发布产物 `start.bat` 增加端口预检【必须】

在原有三项前置检查（`WorkshopMes.exe` 存在、`coreclr.dll` 存在、`runtimeconfig.json` 含 `includedFrameworks`）之后、`echo [OK] Folder:` 之前，插入以下段落（已在 Windows cmd 下实测通过，可直接复制）：

```bat
rem ---------- 防重复启动：8080 已被占用就别再起一个 ----------
set "PORT_PID="
for /f "tokens=5" %%a in ('netstat -ano ^| findstr /C:":8080 " ^| findstr /C:"LISTENING"') do (
  if not defined PORT_PID set "PORT_PID=%%a"
)

set "PORT_NAME="
if defined PORT_PID (
  for /f "tokens=1 delims=," %%b in ('tasklist /FI "PID eq %PORT_PID%" /NH /FO CSV 2^>nul') do set "PORT_NAME=%%~b"
)

if defined PORT_PID (
  echo [提示] 端口 8080 已被占用，不需要再启动一个。
  echo.
  echo         占用进程：%PORT_NAME%（PID=%PORT_PID%）
  echo         直接用就行：https://localhost:8080/
  echo.
  echo         想重启的话：先双击 stop，等窗口显示 Stopped，再双击本脚本。
  echo         如果上面那个进程名不是 WorkshopMes.exe，说明是别的程序占了 8080，
  echo         要先把它关掉，再双击本脚本。
  echo.
  pause
  exit /b 0
)
rem ----------------------------------------------------------
```

约束：
- 已占用 → 打印中文提示后 `pause` 并 `exit /b 0`，**绝不启动第二个实例**。
- 未占用 → 行为必须与现状**完全一致**：同样的启动参数、同样的橙色/绿色提示、同样的退出码处理逻辑。
- 文件编码保持 **UTF-8 带 BOM**（现有 `start.bat` 即 UTF-8 BOM；脚本第 2 行已有 `chcp 65001`，中文提示可正常显示），行尾用 **CRLF**。
- **把这个改动落到生成/存放 `start.bat` 的源头**（发布脚本或模板），不要只手改 `publish-win64` 里的产物；否则下次重新发布又退化。

### B. 程序层：后端友好处理端口占用【建议，改 `Program.cs`】

- 捕获 Kestrel 启动期的 `IOException` / `AddressInUseException`，不要让它变成未处理异常。
- 打印一行中文提示后正常退出，例如：
  `端口 8080 已被占用：后端可能已经在运行，请勿重复启动。如需重启，请先运行 stop.bat。`
- 不输出英文堆栈；退出码固定为 `0` 或一个自定义码（建议 `2`），不要再出现 `0xE0434352`。
- 该提示同时写入 `logs` 目录，便于事后追溯。

### C. 顺带修正【可选】

启动横幅里的 `本机打开 http://localhost:8080/` 协议写错了，实际监听的是 HTTPS，应改为 `https://localhost:8080/`。

## 四、验收标准（逐条实测，缺一不可）

| 序号 | 场景 | 期望结果 |
|---|---|---|
| 1 | 8080 已被占用时双击 `start.bat` | 只出现中文提示；**不出现** `address already in use`、堆栈、`Exit code=-532462766` |
| 2 | 同上 | **不产生新的 `WorkshopMes` 进程**；原实例继续正常服务（`curl -k https://localhost:8080/` 返回 200） |
| 3 | 同上 | 脚本退出码为 `0` |
| 4 | 8080 空闲时双击 `start.bat` | 后端正常启动，原有全部提示不变，浏览器可访问 `https://localhost:8080/` |
| 5 | 反向用例：把检测端口临时改成空闲端口（如 18080） | 不得误判为“已占用”而拒绝启动，应正常进入启动流程 |
| 6 | 重新执行一次发布流程 | 新产物里的 `start.bat` 仍带该判断（证明改在源头，不是只改产物） |

建议的测试环境隔离方式：在临时目录放一份 `start.bat` 副本，配一个立刻退出的假 `WorkshopMes.exe`、空 `coreclr.dll`、含 `includedFrameworks` 的 `runtimeconfig.json`，即可在**不影响真实后端**的前提下跑通分支 1–5。

## 五、明确不要做的事

- 不要改监听端口（保持 8080），不要改 `ASPNETCORE_URLS`、日志目录、`pause` 行为、`stop.bat` 语义。
- 不要采用“检测到占用就自动结束旧进程”的方案：会误杀正在服务的实例，并掩盖真实问题。
- 不要卸载/重装 .NET 运行时，不要改为依赖框架的发布方式。
- 不要在 `start.bat` 里加入 `chcp` 之外的编码改动（如另存为 ANSI/GBK），否则中文提示会乱码。

## 六、参考基线（供开发电脑对照）

- 程序：`.NET 8` 自包含发布（`WorkshopMes.runtimeconfig.json` 内含 `includedFrameworks`），入口 `WorkshopMes.exe`，监听 `https://0.0.0.0:8080`，自签名证书（浏览器提示“不安全”属正常）。
- 健康判定：`GET /` 与 `/swagger/v1/swagger.json` 返回 `200`；`GET /api/factories` 不带 token 返回 `401`（该接口需登录，401 不代表服务异常）。
- 现有 `stop.bat` 会结束该目录下**所有** `WorkshopMes.exe`（含正在服务的那个），因此“重启”的正确姿势是 stop → start。
- 本机已改好并实测通过的对照文件：`D:\WorkShop\backend\publish-win64\start.bat`（2375 字节，UTF-8 BOM + CRLF），原版备份 `start.bat.bak-20261012`（1311 字节）。
