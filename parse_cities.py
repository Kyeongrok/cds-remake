import json
import re
import sys

# cities.txt 파일 읽기
with open('cds-helper/Models/cities.txt', 'r', encoding='utf-8') as f:
    content = f.read()

# 기존 cities.json 읽기
with open('cds-helper/cities.json', 'r', encoding='utf-8') as f:
    cities = json.load(f)

print(f"Loaded {len(cities)} cities from JSON", file=sys.stderr)

# 도시명으로 딕셔너리 생성 (빠른 검색용)
city_dict = {city['name']: city for city in cities}

# 좌표 파싱 함수
def parse_coordinate(coord_str):
    """
    예: "북위41-서경8" -> (41, -8)
    예: "남위7-동경34" -> (-7, 34)
    """
    if not coord_str or coord_str.strip() == '':
        return None, None

    # 패턴: (북위|남위)숫자-(동경|서경)숫자
    match = re.match(r'(북위|남위)(\d+)-(동경|서경)(\d+)', coord_str.strip())
    if not match:
        return None, None

    lat_dir, lat_val, lon_dir, lon_val = match.groups()

    # 위도: 북위는 +, 남위는 -
    lat = int(lat_val) if lat_dir == '북위' else -int(lat_val)

    # 경도: 동경은 +, 서경은 -
    lon = int(lon_val) if lon_dir == '동경' else -int(lon_val)

    return lat, lon

# 텍스트 파일을 라인별로 분석
lines = content.split('\n')
i = 0
current_city = None
coordinate_str = None

while i < len(lines):
    line = lines[i].strip()

    # 빈 줄이나 교역권 헤더는 건너뛰기
    if not line or '교역권' in line or line in ['도시명', '좌표', '특산품', '특징 (도시내 발견물)', '​']:
        i += 1
        continue

    # 도시명으로 추정되는 라인
    # 다음 라인이 좌표 패턴인지 확인
    if i + 1 < len(lines):
        next_line = lines[i + 1].strip()
        if re.match(r'(북위|남위)\d+-(동경|서경)\d+', next_line):
            current_city = line
            coordinate_str = next_line

            # 좌표 파싱
            lat, lon = parse_coordinate(coordinate_str)

            # 도시 찾기 (부분 매칭도 시도)
            if current_city in city_dict:
                city_dict[current_city]['latitude'] = lat
                city_dict[current_city]['longitude'] = lon
                print(f"✓ {current_city}: ({lat}, {lon})")
            else:
                # 유사한 이름 찾기
                found = False
                for city_name in city_dict.keys():
                    if city_name in current_city or current_city in city_name:
                        city_dict[city_name]['latitude'] = lat
                        city_dict[city_name]['longitude'] = lon
                        print(f"✓ {city_name} (from {current_city}): ({lat}, {lon})")
                        found = True
                        break

                if not found:
                    print(f"✗ 못찾음: {current_city}")

            i += 2  # 도시명과 좌표 라인 건너뛰기
            continue

    i += 1

# 업데이트된 cities 리스트 생성
updated_cities = list(city_dict.values())
updated_cities.sort(key=lambda x: x['id'])

# cities.json 업데이트
with open('cds-helper/cities.json', 'w', encoding='utf-8') as f:
    json.dump(updated_cities, f, ensure_ascii=False, indent=2)

print(f"\n총 {len([c for c in updated_cities if 'latitude' in c])}개 도시에 좌표 추가 완료!")
