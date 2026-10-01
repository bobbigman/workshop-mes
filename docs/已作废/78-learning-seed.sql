/* ============================================================================
  docs/78 工序闪卡学习 —— 演示种子（幂等）
  按工序名挂课程：有「切菜/炒制」灌川菜馆卡；有「下料/冲压」灌五金卡。
  须先建表：docs/sql/78-learning-ddl.sql（或等应用启动 DbCompat 自动建）。
  用法：在目标库执行，可重复跑（同厂同课程名已存在则跳过）。
 ============================================================================ */

SET NOCOUNT ON;

DECLARE @fid BIGINT;
DECLARE @cid BIGINT;
DECLARE @opid BIGINT;

DECLARE fac CURSOR LOCAL FAST_FORWARD FOR
  SELECT id FROM sys_factory;
OPEN fac;
FETCH NEXT FROM fac INTO @fid;
WHILE @@FETCH_STATUS = 0
BEGIN
  -- 切菜
  SELECT @opid = id FROM base_operation WHERE factory_id = @fid AND name = N'切菜';
  IF @opid IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM base_learning_course WHERE factory_id = @fid AND name = N'切菜作业指导')
  BEGIN
    INSERT INTO base_learning_course(factory_id, name, operation_id, enabled, sort)
    VALUES (@fid, N'切菜作业指导', @opid, 1, 0);
    SET @cid = SCOPE_IDENTITY();
    INSERT INTO base_learning_card(course_id, factory_id, seq, title, content) VALUES
      (@cid, @fid, 1, N'开工前检查', N'看刀具有没有缺口、砧板干不干净。手要洗干净，围裙系好。刀柄滑手就先擦干再干。'),
      (@cid, @fid, 2, N'切配顺序', N'先切配菜、后切主料。块要大小差不多，方便炒匀。切完分开放，别混堆。'),
      (@cid, @fid, 3, N'安全注意', N'切的时候手指蜷成爪子形，刀背贴指节推进。刀掉了别伸手去接，让它掉地上。'),
      (@cid, @fid, 4, N'收尾清洁', N'切完把案板刮干净，刀具洗净擦干挂好。碎屑扫进垃圾桶，地面留水要擦干防滑。');
    PRINT CONCAT('[OK] factory=', @fid, ' 切菜作业指导');
  END

  -- 炒制
  SET @opid = NULL;
  SELECT @opid = id FROM base_operation WHERE factory_id = @fid AND name = N'炒制';
  IF @opid IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM base_learning_course WHERE factory_id = @fid AND name = N'炒制作业指导')
  BEGIN
    INSERT INTO base_learning_course(factory_id, name, operation_id, enabled, sort)
    VALUES (@fid, N'炒制作业指导', @opid, 1, 0);
    SET @cid = SCOPE_IDENTITY();
    INSERT INTO base_learning_card(course_id, factory_id, seq, title, content) VALUES
      (@cid, @fid, 1, N'开火前', N'确认灶眼正常、油壶盐罐够用。锅要干爽，别带着水倒油。'),
      (@cid, @fid, 2, N'火候与下料', N'油热冒烟再下料。爆香料先下，主料后下。别一次倒太多，炒不开就分两次。'),
      (@cid, @fid, 3, N'出锅标准', N'看颜色、闻香味、尝味道。过咸立即用清水或配菜救，别硬出锅。装盘前关火。'),
      (@cid, @fid, 4, N'安全提醒', N'锅把朝里，别伸到过道。油锅起火盖盖子闷灭，千万别浇水。');
    PRINT CONCAT('[OK] factory=', @fid, ' 炒制作业指导');
  END

  -- 下料
  SET @opid = NULL;
  SELECT @opid = id FROM base_operation WHERE factory_id = @fid AND name = N'下料';
  IF @opid IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM base_learning_course WHERE factory_id = @fid AND name = N'下料工序作业指导')
  BEGIN
    INSERT INTO base_learning_course(factory_id, name, operation_id, enabled, sort)
    VALUES (@fid, N'下料工序作业指导', @opid, 1, 0);
    SET @cid = SCOPE_IDENTITY();
    INSERT INTO base_learning_card(course_id, factory_id, seq, title, content) VALUES
      (@cid, @fid, 1, N'开工前检查', N'看剪板机急停、防护罩是否正常。料厚对照工艺卡，量尺寸再下刀。手套破了先换。'),
      (@cid, @fid, 2, N'放料对齐', N'板料靠紧挡块，边角对齐。手远离刀口，确认周围无人再踩脚踏。'),
      (@cid, @fid, 3, N'尺寸抽查', N'每切 5 张抽一张用卡尺量，超差立即停机找班长。毛刺大的另放一旁。'),
      (@cid, @fid, 4, N'收尾清场', N'废料进废料箱，台面擦干净。断电挂牌，交接班写清已切数量。');
    PRINT CONCAT('[OK] factory=', @fid, ' 下料工序作业指导');
  END

  -- 冲压
  SET @opid = NULL;
  SELECT @opid = id FROM base_operation WHERE factory_id = @fid AND name = N'冲压';
  IF @opid IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM base_learning_course WHERE factory_id = @fid AND name = N'冲压工序作业指导')
  BEGIN
    INSERT INTO base_learning_course(factory_id, name, operation_id, enabled, sort)
    VALUES (@fid, N'冲压工序作业指导', @opid, 1, 0);
    SET @cid = SCOPE_IDENTITY();
    INSERT INTO base_learning_card(course_id, factory_id, seq, title, content) VALUES
      (@cid, @fid, 1, N'模具检查', N'上模下模有无裂纹、螺丝是否拧紧。润滑油加在导柱上，别滴到刃口。'),
      (@cid, @fid, 2, N'试冲三片', N'先手摇试冲三片，看成型和毛刺。OK 再开连续冲。声音异常立刻急停。'),
      (@cid, @fid, 3, N'操作姿势', N'双手拿料，脚不踩空。送料不到位不许伸手进模区，用钩子或镊子。'),
      (@cid, @fid, 4, N'异常处理', N'卡料、连冲、异响：急停 → 断电 → 挂牌 → 报班长。自己拆模前必须确认已断电。');
    PRINT CONCAT('[OK] factory=', @fid, ' 冲压工序作业指导');
  END

  FETCH NEXT FROM fac INTO @fid;
END
CLOSE fac;
DEALLOCATE fac;

PRINT 'done';
