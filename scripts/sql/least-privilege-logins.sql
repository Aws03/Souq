-- ===========================================================================
-- هويّات قاعدة البيانات الثلاث (R-12). يُشغَّل مرة واحدة بهوية إدارية، ثم لا يعود إليها
-- التطبيق أبداً. الصلاحيات هنا ليست تقديراً: قِيست بـ scripts/verify-least-privilege.sh
-- (انظر docs/07-SECURITY/DatabasePrivileges.md).
--
--   sqlcmd -S <server> -U sa -C -i scripts/sql/least-privilege-logins.sql \
--          -v AppPassword="…" -v MigratorPassword="…"
--
-- تُمرَّر كلمات المرور بـ -v ولا تُكتب في هذا الملف. لا افتراضي لها عمداً: كلمة مرور
-- افتراضية في نص مُتتبَّع في Git هي بالضبط ما تحاول هذه الصفحة إزالته.
-- ===========================================================================
:setvar DbName "SouqDb"
:setvar AppLogin "souq_app"
:setvar MigratorLogin "souq_migrator"

SET NOCOUNT ON;
GO

-- قاعدة البيانات تُنشأ هنا، بهوية إدارية. هذا مقصود ومهم: بعد الفصل لا يملك التطبيق
-- ولا المُهاجر صلاحية CREATE DATABASE، فقاعدة غائبة تعني إقلاعاً فاشلاً بوضوح بدل
-- إنشاء صامت لقاعدة فارغة تبدو سليمة.
IF DB_ID(N'$(DbName)') IS NULL
BEGIN
    DECLARE @create nvarchar(max) = N'CREATE DATABASE [$(DbName)]';
    EXEC sp_executesql @create;
END
GO

-- ── هوية التشغيل: تقرأ وتكتب صفوفاً، ولا شيء غير ذلك ──────────────────────
IF SUSER_ID(N'$(AppLogin)') IS NULL
    CREATE LOGIN [$(AppLogin)] WITH PASSWORD = N'$(AppPassword)', CHECK_POLICY = ON;
GO
USE [$(DbName)];
GO
IF USER_ID(N'$(AppLogin)') IS NULL
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)];
GO
ALTER ROLE [db_datareader] ADD MEMBER [$(AppLogin)];
ALTER ROLE [db_datawriter] ADD MEMBER [$(AppLogin)];
GO
-- ولا شيء آخر. لا db_ddladmin: التطبيق لا يغيّر المخطّط في التشغيل العادي.
-- لا db_owner. لا صلاحية على مستوى الخادم. هذه هي النقطة كلها.

-- ── هوية الهجرات: تغيّر المخطّط وتنقل البيانات، وتُستخدم عند الإقلاع وحده ──
IF SUSER_ID(N'$(MigratorLogin)') IS NULL
    CREATE LOGIN [$(MigratorLogin)] WITH PASSWORD = N'$(MigratorPassword)', CHECK_POLICY = ON;
GO
IF USER_ID(N'$(MigratorLogin)') IS NULL
    CREATE USER [$(MigratorLogin)] FOR LOGIN [$(MigratorLogin)];
GO
-- ddladmin للمخطّط، وreader/writer لأن عشر هجرات تنقل بيانات فعلياً (لا DDL وحده).
ALTER ROLE [db_ddladmin]   ADD MEMBER [$(MigratorLogin)];
ALTER ROLE [db_datareader] ADD MEMBER [$(MigratorLogin)];
ALTER ROLE [db_datawriter] ADD MEMBER [$(MigratorLogin)];
GO
-- ولا db_owner: لا يستطيع هذا الحساب حذف القاعدة، ولا منح صلاحيات، ولا تغيير هوية أخرى.

-- ── الهوية الإدارية ───────────────────────────────────────────────────────
-- ليست هنا: هي حساب بشري (أو حساب طوارئ في مخزن الأسرار) يُستخدم لتشغيل هذا الملف،
-- وللاستعادة، وللصيانة. لا يُوضع في أي سلسلة اتصال لتطبيق.
