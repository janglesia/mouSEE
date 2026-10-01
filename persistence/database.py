import sqlite3

# Connect to database
connection = sqlite3.connect("mousee.db")
cursor = connection.cursor()
connection.execute("PRAGMA foreign_keys = ON")

# Tables/Entities
#Drop tabkes if they already exist
cursor.execute("DROP TABLE IF EXISTS CalibrationData")
cursor.execute("DROP TABLE IF EXISTS Session")
cursor.execute("DROP TABLE IF EXISTS Settings")
cursor.execute("DROP TABLE IF EXISTS User")

# User profiles table creation
users_creation_query = """
    CREATE TABLE User (
        user_id INTEGER PRIMARY KEY AUTOINCREMENT,
        username TEXT NOT NULL,
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
"""

# Settings table creation
# User + Settings have 1:1 relationship, so UNIQUE constraint is added to user_id column
settings_creation_query = """
    CREATE TABLE Settings (
        settings_id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_id INTEGER NOT NULL UNIQUE,
        dwell_time_ms INTEGER NOT NULL DEFAULT 1000,
        click_method TEXT NOT NULL DEFAULT 'dwell',

        FOREIGN KEY (user_id) REFERENCES User(user_id)
            ON DELETE CASCADE 
            ON UPDATE CASCADE,
        CHECK (click_method IN ('dwell', 'blink')),
        CHECK (dwell_time_ms > 0)
    );
"""

# Calibration sessions table creation
session_creation_query = """
    CREATE TABLE Session (
        session_id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_id INTEGER NOT NULL,
        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
        screen_height INTEGER NOT NULL,
        screen_width INTEGER NOT NULL,

        FOREIGN KEY (user_id) REFERENCES User(user_id)
            ON DELETE CASCADE 
            ON UPDATE CASCADE
    );
"""

# Calibration points table creation - table not finalized
calibration_data_query = """
    CREATE TABLE CalibrationData (
        calibration_data_id INTEGER PRIMARY KEY AUTOINCREMENT,
        session_id INTEGER NOT NULL,

        target_x REAL NOT NULL,
        target_y REAL NOT NULL,

        gaze_x REAL NOT NULL,
        gaze_y REAL NOT NULL,

        FOREIGN KEY (session_id) REFERENCES Session(session_id)
            ON DELETE CASCADE 
            ON UPDATE CASCADE
    );
"""

# Execute the table creation queries
cursor.execute(users_creation_query)
cursor.execute(settings_creation_query)
cursor.execute(session_creation_query)
cursor.execute(calibration_data_query)

# Confirm that the tables have been created
print("Tables are Ready")

# Commit changes
connection.commit()


# Example Data for Insertion ----------
# Users
cursor.execute("INSERT INTO User VALUES (NULL, 'Shahana', NULL)")
cursor.execute("INSERT INTO User VALUES (NULL, 'Smith', NULL)")
cursor.execute("INSERT INTO User VALUES (NULL, 'Johnson', NULL)")
cursor.execute("INSERT INTO User VALUES (NULL, 'Bob', NULL)")

print("\nData Inserted in the User table: ")
cursor.execute("SELECT * FROM User")
for row in cursor.fetchall():
    print(row)

# Settings
cursor.execute("INSERT INTO Settings VALUES (NULL, 1, 1000, 'dwell')")
cursor.execute("INSERT INTO Settings VALUES (NULL, 2, 1500, 'blink')")
cursor.execute("INSERT INTO Settings VALUES (NULL, 3, 800, 'dwell')")
cursor.execute("INSERT INTO Settings VALUES (NULL, 4, 1200, 'dwell')")

print("\nData Inserted in the Settings table: ")
cursor.execute("SELECT * FROM Settings")
for row in cursor.fetchall():
    print(row)

# Calibration Sessions
cursor.execute("INSERT INTO Session (user_id, screen_height, screen_width) VALUES (1, 1080, 1920)")
cursor.execute("INSERT INTO Session (user_id, screen_height, screen_width) VALUES (2, 1080, 1920)")
cursor.execute("INSERT INTO Session (user_id, screen_height, screen_width) VALUES (3, 1440, 2560)")
cursor.execute("INSERT INTO Session (user_id, screen_height, screen_width) VALUES (4, 1080, 1920)")

print("\nData Inserted in the Session table: ")
cursor.execute("SELECT * FROM Session")
for row in cursor.fetchall():
    print(row)


# Close connection
connection.close()
