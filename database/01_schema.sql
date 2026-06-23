-- ============================================================================
--  متجر "سوق" — مخطّط قاعدة البيانات لـ SQL Server
--  شغّله في Azure Data Studio على اتصال SQL Server.
--
--  ملاحظة: مشروع .NET يُنشئ هذه الجداول تلقائياً عبر EF Core Migrations،
--  لكن نوفّر هذا السكربت اليدوي حتى تفهم الترجمة من "الكيانات" إلى "الجداول"
--  بوضوح، وتستطيع تشغيل قاعدة البيانات مستقلّة عن التطبيق.
-- ============================================================================

IF DB_ID('SouqDb') IS NULL CREATE DATABASE SouqDb;
GO
USE SouqDb;
GO

-- ── جدول الفئات ──────────────────────────────────────────────────────────
-- ParentId يشير لنفس الجدول (علاقة ذاتية) لدعم الفئات المتفرّعة مستقبلاً.
CREATE TABLE Categories (
    Id        INT IDENTITY(1,1) PRIMARY KEY,   -- مفتاح متسلسل تلقائي
    Name      NVARCHAR(100) NOT NULL,          -- NVARCHAR لدعم العربية (Unicode)
    Slug      NVARCHAR(100) NOT NULL,
    ParentId  INT NULL REFERENCES Categories(Id),
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL
);
-- الـ Slug فريد لأنه يُستخدم في الروابط (/category/electronics).
CREATE UNIQUE INDEX UX_Categories_Slug ON Categories(Slug);
GO

-- ── جدول المنتجات ────────────────────────────────────────────────────────
-- لاحظ: السعر عمودان (Price + Currency) لأن كائن القيمة Money في الكود
-- يجمع المبلغ والعملة. IsActive للحذف المنطقي (لا نحذف منتجاً فعلياً).
CREATE TABLE Products (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    Name          NVARCHAR(200) NOT NULL,
    Description   NVARCHAR(2000) NULL,
    Price         DECIMAL(18,2) NOT NULL,       -- DECIMAL لا FLOAT (دقّة مالية)
    Currency      NVARCHAR(3) NOT NULL DEFAULT 'JOD',
    StockQuantity INT NOT NULL DEFAULT 0,
    ImageUrl      NVARCHAR(500) NULL,
    IsActive      BIT NOT NULL DEFAULT 1,        -- الحذف المنطقي
    CategoryId    INT NOT NULL REFERENCES Categories(Id),
    CreatedAt     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt     DATETIME2 NULL
);
-- فهرس على CategoryId لأن أكثر استعلام شيوعاً هو "منتجات هذه الفئة".
CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);
GO

-- ── جدول العملاء ─────────────────────────────────────────────────────────
-- نخزّن PasswordHash لا كلمة المرور الخام أبداً (أمان).
CREATE TABLE Customers (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    FullName     NVARCHAR(150) NOT NULL,
    Email        NVARCHAR(256) NOT NULL,
    PasswordHash NVARCHAR(500) NOT NULL,
    Role         NVARCHAR(20) NOT NULL DEFAULT 'Customer',
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt    DATETIME2 NULL
);
CREATE UNIQUE INDEX UX_Customers_Email ON Customers(Email);
GO

-- ── جدول الطلبات ─────────────────────────────────────────────────────────
-- Status رقم يمثّل الـ enum (0=Pending,1=Paid,2=Shipped,3=Delivered,4=Cancelled).
CREATE TABLE Orders (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId      INT NOT NULL REFERENCES Customers(Id),
    Status          INT NOT NULL DEFAULT 0,
    ShippingAddress NVARCHAR(500) NOT NULL,
    CreatedAt       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2 NULL
);
CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
GO

-- ── جدول أسطر الطلب ──────────────────────────────────────────────────────
-- ★ القرار الأهم: نخزّن ProductName و UnitPrice هنا (مكرّرين من Products)
--   عمداً، لأن الفاتورة يجب أن تبقى مجمّدة بسعر لحظة الشراء حتى لو تغيّر السعر.
-- ON DELETE CASCADE: حذف الطلب يحذف أسطره (لأنها جزء من تجمّعه).
CREATE TABLE OrderItems (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    OrderId     INT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
    ProductId   INT NOT NULL REFERENCES Products(Id),
    ProductName NVARCHAR(200) NOT NULL,          -- لقطة مجمّدة
    UnitPrice   DECIMAL(18,2) NOT NULL,          -- لقطة مجمّدة
    Currency    NVARCHAR(3) NOT NULL DEFAULT 'JOD',
    Quantity    INT NOT NULL,
    CreatedAt   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2 NULL
);
CREATE INDEX IX_OrderItems_OrderId ON OrderItems(OrderId);
GO
