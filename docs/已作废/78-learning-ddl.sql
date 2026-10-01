/* ============================================================================
  docs/78 工序闪卡学习 —— 建表（幂等）
  生产库可手工执行；应用启动也会经 DbCompat 补建。
  部署顺序：先跑本脚本 →（可选）再跑 78-learning-seed.sql 灌演示内容。
 ============================================================================ */

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'base_learning_course')
BEGIN
  CREATE TABLE base_learning_course (
    id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_base_learning_course PRIMARY KEY,
    factory_id   BIGINT NOT NULL,
    name         NVARCHAR(100) NOT NULL,
    product_id   BIGINT NULL,
    operation_id BIGINT NULL,
    enabled      BIT NOT NULL CONSTRAINT DF_base_learning_course_en DEFAULT 1,
    sort         INT NOT NULL CONSTRAINT DF_base_learning_course_sort DEFAULT 0,
    created_at   DATETIME2 NOT NULL CONSTRAINT DF_base_learning_course_ca DEFAULT SYSDATETIME()
  );
  PRINT '[OK] base_learning_course';
END
ELSE PRINT '[跳过] base_learning_course';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'base_learning_card')
BEGIN
  CREATE TABLE base_learning_card (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_base_learning_card PRIMARY KEY,
    course_id   BIGINT NOT NULL,
    factory_id  BIGINT NOT NULL,
    seq         INT NOT NULL,
    title       NVARCHAR(200) NOT NULL,
    content     NVARCHAR(4000) NOT NULL,
    created_at  DATETIME2 NOT NULL CONSTRAINT DF_base_learning_card_ca DEFAULT SYSDATETIME()
  );
  CREATE INDEX ix_learning_card_course ON base_learning_card(course_id, seq);
  PRINT '[OK] base_learning_card';
END
ELSE PRINT '[跳过] base_learning_card';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'user_learning_progress')
BEGIN
  CREATE TABLE user_learning_progress (
    id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_user_learning_progress PRIMARY KEY,
    user_id      BIGINT NOT NULL,
    course_id    BIGINT NOT NULL,
    last_card_id BIGINT NOT NULL,
    updated_at   DATETIME2 NOT NULL CONSTRAINT DF_user_learning_progress_ua DEFAULT SYSDATETIME()
  );
  CREATE UNIQUE INDEX ux_learning_progress ON user_learning_progress(user_id, course_id);
  PRINT '[OK] user_learning_progress';
END
ELSE PRINT '[跳过] user_learning_progress';

PRINT 'done';
