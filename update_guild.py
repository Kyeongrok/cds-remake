import csv
import sqlite3

def update_guild_from_csv():
    csv_path = r"C:\Users\ocean\git\cds-helper\CdsHelper\도시.csv"
    db_path = r"C:\Users\ocean\git\cds-helper\CdsHelper\bin\Debug\net8.0-windows\cdshelper.db"

    # CSV 읽기
    guild_data = {}
    with open(csv_path, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            city_name = row['도시명'].strip()
            has_guild = row['조합'].strip() == '○'
            guild_data[city_name] = has_guild

    print(f"CSV에서 {len(guild_data)}개 도시 로드됨")

    # 조합 있는 도시들
    guild_cities = [name for name, has_guild in guild_data.items() if has_guild]
    print(f"조합 있는 도시: {len(guild_cities)}개")
    print(guild_cities[:10], "...")

    # DB 업데이트
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()

    # HasGuild 컬럼이 없으면 추가
    cursor.execute("PRAGMA table_info(Cities)")
    columns = [col[1] for col in cursor.fetchall()]
    if 'HasGuild' not in columns:
        print("HasGuild 컬럼 추가 중...")
        cursor.execute("ALTER TABLE Cities ADD COLUMN HasGuild INTEGER DEFAULT 0")

    # 모든 도시 조회
    cursor.execute("SELECT Id, Name FROM Cities")
    cities = cursor.fetchall()

    updated = 0
    for city_id, city_name in cities:
        has_guild = guild_data.get(city_name, False)
        cursor.execute("UPDATE Cities SET HasGuild = ? WHERE Id = ?", (1 if has_guild else 0, city_id))
        if has_guild:
            updated += 1

    conn.commit()
    conn.close()

    print(f"DB 업데이트 완료: {updated}개 도시에 조합 설정됨")

if __name__ == "__main__":
    update_guild_from_csv()
