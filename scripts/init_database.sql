/*
    Script d'initialisation de la base de données OpcUaData
    Ce script crée les tables, les types et insère des données de test pour le simulateur.
*/

-- 1. Création de la base de données (si elle n'existe pas)
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'OpcUaData')
BEGIN
    CREATE DATABASE OpcUaData;
END
GO

USE OpcUaData;
GO

-- 2. Création des tables
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Machines]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Machines] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(255),
        [AutomateType] NVARCHAR(50),
        [OpcEndpoint] NVARCHAR(255),
        [IsActive] BIT DEFAULT 1,
        [CreatedAt] DATETIME DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME DEFAULT GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Tags]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Tags] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [MachineId] INT REFERENCES Machines(Id),
        [Name] NVARCHAR(100) NOT NULL,
        [NodeId] NVARCHAR(255) NOT NULL,
        [DataType] NVARCHAR(50) NOT NULL,
        [Unit] NVARCHAR(20),
        [MinValue] FLOAT,
        [MaxValue] FLOAT,
        [AllowedValues] NVARCHAR(MAX),
        [IsActive] BIT DEFAULT 1,
        [CreatedAt] DATETIME DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME DEFAULT GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TagValues]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[TagValues] (
        [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [MachineId] INT,
        [TagId] INT,
        [TagName] NVARCHAR(100),
        [NodeId] NVARCHAR(255),
        [Value] NVARCHAR(MAX),
        [Quality] NVARCHAR(50),
        [SourceTimestamp] DATETIME,
        [ServerTimestamp] DATETIME,
        [ReceivedTimestamp] DATETIME DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_TagValues_ReceivedTimestamp ON TagValues(ReceivedTimestamp);
END
GO

-- 3. Création du type pour l'insertion batch (TVP)
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TagValueTableType' AND is_table_type = 1)
BEGIN
    CREATE TYPE [dbo].[TagValueTableType] AS TABLE (
        [MachineId] INT,
        [TagId] INT,
        [TagName] NVARCHAR(100),
        [NodeId] NVARCHAR(255),
        [Value] NVARCHAR(MAX),
        [Quality] NVARCHAR(50),
        [SourceTimestamp] DATETIME,
        [ServerTimestamp] DATETIME,
        [ReceivedTimestamp] DATETIME
    );
END
GO

-- 4. Création de la Stored Procedure pour l'insertion batch
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_InsertTagValuesBatch]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_InsertTagValuesBatch];
END
GO

CREATE PROCEDURE [dbo].[usp_InsertTagValuesBatch]
    @TagValues [dbo].[TagValueTableType] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [dbo].[TagValues] (
        MachineId, TagId, TagName, NodeId, Value, Quality, 
        SourceTimestamp, ServerTimestamp, ReceivedTimestamp
    )
    SELECT 
        MachineId, TagId, TagName, NodeId, Value, Quality, 
        SourceTimestamp, ServerTimestamp, ReceivedTimestamp
    FROM @TagValues;
END
GO

-- 5. Insertion de données de test (Simulateur)
IF NOT EXISTS (SELECT * FROM Machines WHERE Name = 'Machine_Test_01')
BEGIN
    INSERT INTO Machines (Name, Description, AutomateType, OpcEndpoint, IsActive)
    VALUES ('Machine_Test_01', 'Machine virtuelle pour simulation', 'Simulated', 'opc.tcp://localhost:4840', 1);

    DECLARE @MachineId INT = SCOPE_IDENTITY();

    INSERT INTO Tags (MachineId, Name, NodeId, DataType, MinValue, MaxValue, IsActive)
    VALUES 
    (@MachineId, 'Temperature', 'ns=2;s=Temperature', 'Float', 20.0, 100.0, 1),
    (@MachineId, 'Vibration', 'ns=2;s=Vibration', 'Float', 0.0, 5.0, 1),
    (@MachineId, 'Status', 'ns=2;s=Status', 'String', NULL, NULL, 1),
    (@MachineId, 'Counter', 'ns=2;s=Counter', 'Int32', 0, 10000, 1);
END
GO
