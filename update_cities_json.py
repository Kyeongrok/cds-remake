import sqlite3
import json

def update_cities_json():
    db_path = "cds-helper/bin/Debug/net8.0-windows/cdshelper.db"
    json_path = "cds-helper/cities.json"

    # DB에서 도시 정보 읽기
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    cursor.execute("SELECT Id, Name, PixelX, PixelY, HasLibrary FROM Cities")
    db_cities = cursor.fetchall()
    conn.close()

    # ID로 매핑
    city_data = {}
    for city_id, name, pixel_x, pixel_y, has_library in db_cities:
        city_data[city_id] = {
            "pixelX": pixel_x,
            "pixelY": pixel_y,
            "hasLibrary": bool(has_library)
        }

    print(f"DB에서 {len(db_cities)}개 도시 로드됨")

    # JSON 파일 읽기
    with open(json_path, 'r', encoding='utf-8') as f:
        cities = json.load(f)

    print(f"JSON에서 {len(cities)}개 도시 로드됨")

    # 업데이트
    updated_count = 0
    for city in cities:
        city_id = city.get("id")
        if city_id in city_data:
            db_info = city_data[city_id]

            # pixelX, pixelY 업데이트
            if db_info["pixelX"] is not None:
                city["pixelX"] = db_info["pixelX"]
            if db_info["pixelY"] is not None:
                city["pixelY"] = db_info["pixelY"]

            # hasLibrary 업데이트
            city["hasLibrary"] = db_info["hasLibrary"]

            updated_count += 1

    print(f"{updated_count}개 도시 업데이트됨")

    # JSON 파일 저장
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(cities, f, ensure_ascii=False, indent=2)

    print(f"JSON 파일 저장 완료: {json_path}")

    # 통계
    with_coords = sum(1 for c in cities if c.get("pixelX") and c.get("pixelY"))
    print(f"좌표가 있는 도시: {with_coords}개")

if __name__ == "__main__":
    update_cities_json()
