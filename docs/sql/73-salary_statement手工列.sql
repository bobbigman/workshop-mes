/* ============================================================================
  docs/73 工资单手工列（底薪/餐补/其他补贴/社保个税/其他扣款）
  幂等：列已存在则跳过。生产库可手工执行；应用启动也会经 DbCompat 补列。
 ============================================================================ */

IF COL_LENGTH('salary_statement', 'base_salary') IS NULL
BEGIN
  ALTER TABLE salary_statement ADD base_salary DECIMAL(12,2) NOT NULL
    CONSTRAINT DF_salary_statement_base DEFAULT 0;
  PRINT '[OK] base_salary';
END
ELSE PRINT '[跳过] base_salary';

IF COL_LENGTH('salary_statement', 'meal_allowance') IS NULL
BEGIN
  ALTER TABLE salary_statement ADD meal_allowance DECIMAL(12,2) NOT NULL
    CONSTRAINT DF_salary_statement_meal DEFAULT 0;
  PRINT '[OK] meal_allowance';
END
ELSE PRINT '[跳过] meal_allowance';

IF COL_LENGTH('salary_statement', 'other_allowance') IS NULL
BEGIN
  ALTER TABLE salary_statement ADD other_allowance DECIMAL(12,2) NOT NULL
    CONSTRAINT DF_salary_statement_oall DEFAULT 0;
  PRINT '[OK] other_allowance';
END
ELSE PRINT '[跳过] other_allowance';

IF COL_LENGTH('salary_statement', 'social_tax') IS NULL
BEGIN
  ALTER TABLE salary_statement ADD social_tax DECIMAL(12,2) NOT NULL
    CONSTRAINT DF_salary_statement_tax DEFAULT 0;
  PRINT '[OK] social_tax';
END
ELSE PRINT '[跳过] social_tax';

IF COL_LENGTH('salary_statement', 'other_deduction') IS NULL
BEGIN
  ALTER TABLE salary_statement ADD other_deduction DECIMAL(12,2) NOT NULL
    CONSTRAINT DF_salary_statement_oded DEFAULT 0;
  PRINT '[OK] other_deduction';
END
ELSE PRINT '[跳过] other_deduction';
GO
