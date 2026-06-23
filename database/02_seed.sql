-- ============================================================================
--  بيانات أولية للتجربة. شغّله بعد 01_schema.sql
-- ============================================================================
USE SouqDb;
GO

INSERT INTO Categories (Name, Slug) VALUES
(N'إلكترونيات', 'electronics'),
(N'أزياء',      'fashion'),
(N'منزل',       'home');
GO

DECLARE @elec INT = (SELECT Id FROM Categories WHERE Slug='electronics');
DECLARE @fash INT = (SELECT Id FROM Categories WHERE Slug='fashion');
DECLARE @home INT = (SELECT Id FROM Categories WHERE Slug='home');

INSERT INTO Products (Name, Description, Price, StockQuantity, ImageUrl, CategoryId) VALUES
(N'سمّاعات لاسلكية',        N'صوت نقي وعزل ضوضاء فعّال',          59.900, 25, 'headphones', @elec),
(N'ساعة ذكية',              N'تتبّع اللياقة والإشعارات',           120.000,12, 'watch',      @elec),
(N'لوحة مفاتيح ميكانيكية',  N'إضاءة خلفية ومفاتيح مريحة',          45.500, 30, 'keyboard',   @elec),
(N'حقيبة ظهر جلدية',        N'تصميم أنيق ومتين للعمل والسفر',      35.000, 18, 'backpack',   @fash),
(N'نظّارة شمسية',           N'حماية UV وإطار خفيف',               22.000, 40, 'sunglasses', @fash),
(N'مصباح مكتب LED',         N'إضاءة قابلة للتعديل وموفّرة للطاقة', 18.750, 50, 'lamp',       @home),
(N'ركوة قهوة نحاسية',       N'صناعة يدوية لقهوة عربية أصيلة',      28.000, 15, 'coffeepot',  @home),
(N'كوب حراري',              N'يحفظ الحرارة 12 ساعة',              14.500, 60, 'mug',        @home);
GO

INSERT INTO Customers (FullName, Email, PasswordHash, Role) VALUES
(N'مدير المتجر', 'admin@souq.com', 'HASHED_admin123', 'Admin');
GO

PRINT N'تم إدخال البيانات الأولية بنجاح ✓';
