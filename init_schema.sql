-- =============================================================
-- TRIP DISTRIBUTION DATABASE SCHEMA (PRODUCTION DDL)
-- Database: SPLIT_BILL_DB
-- =============================================================

-- Table: Users
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE [Users] (
        [user_id] nvarchar(100) NOT NULL,
        [phone_number] nvarchar(20) NOT NULL,
        [full_name] nvarchar(150) NULL,
        [password_hash] nvarchar(255) NULL,
        [auth_token] nvarchar(MAX) NULL,
        [token_expiry] datetime NULL,
        [diet_type] int NULL,
        [diet_name] nvarchar(50) NULL,
        [avatar_color] nvarchar(20) NULL,
        [is_biometric_enabled] bit NULL,
        [is_active] bit NULL,
        [created_at] datetime NULL,
        [updated_at] datetime NULL,
        [last_login_at] datetime NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([user_id])
    );
END;

-- Table: OtpVerification
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OtpVerification')
BEGIN
    CREATE TABLE [OtpVerification] (
        [otp_id] nvarchar(100) NOT NULL,
        [phone_number] nvarchar(20) NOT NULL,
        [otp_code] nvarchar(10) NOT NULL,
        [purpose] nvarchar(50) NULL,
        [is_verified] bit NULL,
        [expires_at] datetime NOT NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_OtpVerification] PRIMARY KEY ([otp_id])
    );
END;

-- Table: FriendConnections
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FriendConnections')
BEGIN
    CREATE TABLE [FriendConnections] (
        [connection_id] nvarchar(100) NOT NULL,
        [user_id] nvarchar(100) NOT NULL,
        [friend_user_id] nvarchar(100) NULL,
        [friend_phone] nvarchar(20) NOT NULL,
        [friend_name] nvarchar(150) NOT NULL,
        [diet_type] int NULL,
        [diet_name] nvarchar(50) NULL,
        [status] nvarchar(50) NULL,
        [requester_id] nvarchar(100) NULL,
        [receiver_id] nvarchar(100) NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_FriendConnections] PRIMARY KEY ([connection_id])
    );
END;

-- Table: Trips
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Trips')
BEGIN
    CREATE TABLE [Trips] (
        [trip_id] nvarchar(100) NOT NULL,
        [user_id] nvarchar(100) NOT NULL,
        [name] nvarchar(200) NOT NULL,
        [description] nvarchar(MAX) NULL,
        [currency_code] nvarchar(10) NULL,
        [created_at] datetime NULL,
        [updated_at] datetime NULL,
        CONSTRAINT [PK_Trips] PRIMARY KEY ([trip_id])
    );
END;

-- Table: TripCollaborators
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TripCollaborators')
BEGIN
    CREATE TABLE [TripCollaborators] (
        [collab_id] nvarchar(100) NOT NULL,
        [trip_id] nvarchar(100) NOT NULL,
        [user_id] nvarchar(100) NULL,
        [person_id] nvarchar(100) NULL,
        [phone_number] nvarchar(20) NULL,
        [role] nvarchar(50) NULL,
        [status] nvarchar(50) NULL,
        [joined_at] datetime NULL,
        CONSTRAINT [PK_TripCollaborators] PRIMARY KEY ([collab_id])
    );
END;

-- Table: Groups
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Groups')
BEGIN
    CREATE TABLE [Groups] (
        [group_id] nvarchar(100) NOT NULL,
        [trip_id] nvarchar(100) NOT NULL,
        [name] nvarchar(200) NOT NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_Groups] PRIMARY KEY ([group_id])
    );
END;

-- Table: Persons
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Persons')
BEGIN
    CREATE TABLE [Persons] (
        [person_id] nvarchar(100) NOT NULL,
        [group_id] nvarchar(100) NULL,
        [trip_id] nvarchar(100) NOT NULL,
        [user_id] nvarchar(100) NULL,
        [phone_number] nvarchar(20) NULL,
        [name] nvarchar(150) NOT NULL,
        [diet_type] int NULL,
        [diet_name] nvarchar(50) NULL,
        [paid_amount] decimal(18, 2) NULL,
        [owed_amount] decimal(18, 2) NULL,
        [balance] decimal(18, 2) NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_Persons] PRIMARY KEY ([person_id])
    );
END;

-- Table: Expenses
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Expenses')
BEGIN
    CREATE TABLE [Expenses] (
        [expense_id] nvarchar(100) NOT NULL,
        [trip_id] nvarchar(100) NOT NULL,
        [description] nvarchar(500) NOT NULL,
        [amount] decimal(18, 2) NOT NULL,
        [currency_code] nvarchar(10) NULL,
        [category_index] int NULL,
        [category_name] nvarchar(100) NULL,
        [paid_by_person_id] nvarchar(100) NOT NULL,
        [split_type] nvarchar(50) NULL,
        [expense_date] datetime NOT NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_Expenses] PRIMARY KEY ([expense_id])
    );
END;

-- Table: ExpenseSplits
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExpenseSplits')
BEGIN
    CREATE TABLE [ExpenseSplits] (
        [split_id] nvarchar(100) NOT NULL,
        [expense_id] nvarchar(100) NOT NULL,
        [trip_id] nvarchar(100) NOT NULL,
        [person_id] nvarchar(100) NOT NULL,
        [amount] decimal(18, 2) NOT NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_ExpenseSplits] PRIMARY KEY ([split_id])
    );
END;

-- Table: SettlementTransactions
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SettlementTransactions')
BEGIN
    CREATE TABLE [SettlementTransactions] (
        [settlement_id] nvarchar(100) NOT NULL,
        [trip_id] nvarchar(100) NOT NULL,
        [from_person_id] nvarchar(100) NOT NULL,
        [to_person_id] nvarchar(100) NOT NULL,
        [amount] decimal(18, 2) NOT NULL,
        [is_completed] bit NULL,
        [status] nvarchar(50) NULL,
        [created_by_user_id] nvarchar(100) NULL,
        [settled_at] datetime NULL,
        CONSTRAINT [PK_SettlementTransactions] PRIMARY KEY ([settlement_id])
    );
END;

-- Table: Notifications
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
BEGIN
    CREATE TABLE [Notifications] (
        [notification_id] nvarchar(100) NOT NULL,
        [user_id] nvarchar(100) NOT NULL,
        [trip_id] nvarchar(100) NULL,
        [trip_name] nvarchar(200) NULL,
        [title] nvarchar(200) NOT NULL,
        [message] nvarchar(MAX) NOT NULL,
        [type] nvarchar(50) NOT NULL,
        [amount] decimal(18, 2) NULL,
        [is_read] bit NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([notification_id])
    );
END;

-- Table: ActivityLogs
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ActivityLogs')
BEGIN
    CREATE TABLE [ActivityLogs] (
        [log_id] nvarchar(100) NOT NULL,
        [user_id] nvarchar(100) NULL,
        [trip_id] nvarchar(100) NULL,
        [action_type] nvarchar(100) NOT NULL,
        [details] nvarchar(MAX) NULL,
        [created_at] datetime NULL,
        CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([log_id])
    );
END;


