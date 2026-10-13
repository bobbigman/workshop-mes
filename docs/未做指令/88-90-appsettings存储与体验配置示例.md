# 88+90 配套 · appsettings 配置示例（存储配额 + 体验账套清理）

> 日期：2026-09-29｜用途：把指令 88（存储配额硬规则）与 90（体验账套到期清理）的所有配置项收成一份，**可直接粘贴进 Cursor 的 `appsettings*.json`**。
> 红线：**这是"示例片段"，只并入现有配置，绝不整文件替换**；部署增量包时**不覆盖服务器已配的 `appsettings*.json`**（见根目录 `DEPLOYMENT_NOTES.md`）。

---

## 一、可直接粘贴的 JSON 片段（并入现有 appsettings 相应节点）

```json
{
  // ===== 私有买断版识别（指令 88）=====
  // 私有版部署时置 true → 存储/单文件全放行；SaaS 云保持 false（唯一权威识别，网址仅辅助）
  "Deployment": {
    "Private": false
  },

  // ===== 知识库存储（指令 88）=====
  "KnowledgeBase": {
    "RootPath": "D:\\workspace\\upload",   // 现有值保留，勿改
    "EnforceQuota": true                    // 配额总开关；false = 一键关闭校验，仍记日志
  },

  // ===== 各档配额：总容量 + 单文件上限（指令 88 唯一事实源，代码不写死）=====
  "StorageLimits": {
    "trial":      { "TotalBytes": 209715200,  "MaxFileBytes": 10485760 },  // 200M / 10M
    "enterprise": { "TotalBytes": 1073741824, "MaxFileBytes": 10485760 },  // 1G  / 10M
    "flagship":   { "TotalBytes": 1073741824, "MaxFileBytes": 10485760 }   // 1G  / 10M
  },

  // ===== 自己云服务器域名白名单（网址仅辅助展示/审计，不参与权限判断）=====
  "KnownCloudHosts": [
    "cloud.yourdomain.com"
  ],

  // ===== 体验账套到期清理（指令 90）=====
  "TrialCleanup": {
    "Enabled": true,        // 清理总开关；false = 跳过清理，仍记"未清理"日志
    "IntervalMinutes": 60,  // 后台扫描间隔
    "GraceMinutes": 0,      // 到期后再留宽限；0 = 到点即清
    "DurationDays": 7       // 体验期天数（创建时 trial_expires_at_utc = 创建 + 此值）
  }
}
```

> 说明：`TotalBytes` / `MaxFileBytes` 用**字节**。换算：1G=1073741824、10M=10485760、200M=209715200。

---

## 二、每项含义（对照指令）

| 配置项 | 默认 | 管什么 | 出处 |
|---|---|---|---|
| `Deployment:Private` | false | 私有买断版识别（true=放行存储/单文件） | 88 §四步骤3 |
| `KnowledgeBase:EnforceQuota` | true | 配额总开关（false=关闭校验但照记日志） | 88 §四步骤5 |
| `StorageLimits.trial.TotalBytes` | 200M | 体验账套总容量 | 88+90 |
| `StorageLimits.trial.MaxFileBytes` | 10M | 体验账套单文件上限 | 88+90 |
| `StorageLimits.enterprise/flagship.TotalBytes` | 1G | 正式客户总容量 | 88 |
| `StorageLimits.enterprise/flagship.MaxFileBytes` | 10M | 正式客户单文件上限 | 88 |
| `KnownCloudHosts` | — | 网址辅助展示/审计白名单（不进权限判断） | 88 §四步骤3 |
| `TrialCleanup.Enabled` | true | 体验账套到期清理开关 | 90 §四步骤5 |
| `TrialCleanup.IntervalMinutes` | 60 | 后台扫描间隔 | 90 §四步骤3 |
| `TrialCleanup.GraceMinutes` | 0 | 到期后宽限 | 90 §四步骤3 |
| `TrialCleanup.DurationDays` | 7 | 体验期天数 | 90 §三/步骤2 |

---

## 三、改一处生效点（Cursor 落地的验证口诀）

- 改 `StorageLimits.*` → 重测一次上传，确认跟随配置而非代码写死；
- 改 `TrialCleanup.DurationDays` → 新建体验账套，看到期时间 = 创建 + 新值；
- `Deployment.Private=true` → 存储/单文件全放行（无限制）；
- `EnforceQuota=false` / `TrialCleanup.Enabled=false` → 都仍留"超限/未清理"日志，不脱敏。
