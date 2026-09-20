# 대항해시대3 세이브 뷰어

[![Release](https://img.shields.io/github/v/release/Kyeongrok/cds-helper?logo=github)](https://github.com/Kyeongrok/cds-helper/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Kyeongrok/cds-helper/total?color=brightgreen)](https://github.com/Kyeongrok/cds-helper/releases)

<img width="272" height="384" alt="image" src="https://github.com/user-attachments/assets/59015153-9700-4752-815e-68093774bfc2" />


대항해시대3 SAVEDATA.CDS 파일을 읽어서 캐릭터 정보를 표시하는 WPF 애플리케이션입니다.

## 주요 기능

- SAVEDATA.CDS 파일 읽기
- 캐릭터 정보 표시 (이름, 능력치, 특기, 명성, 소재, 연령, 얼굴, 성좌)
- 미등장 캐릭터 필터링 (18세 미만 또는 60세 초과)
- 이름 검색 기능
- 특기별 레벨 필터링 (26개 특기, 레벨 1-9)

## 빌드 방법

### 바탕화면에 빌드하기

```bash
cd C:\Users\ocean\git\cds-helper\cds-helper
dotnet publish -c Release -o "C:\Users\ocean\Desktop\대항해시대3뷰어"
```

빌드 완료 후 `C:\Users\ocean\Desktop\대항해시대3뷰어` 폴더에 실행 파일이 생성됩니다.

실행 파일: `cds-helper.exe`

### 개발 빌드

```bash
cd C:\Users\ocean\git\cds-helper\cds-helper
dotnet build
```

### 실행

```bash
cd C:\Users\ocean\git\cds-helper\cds-helper
dotnet run
```

## 필요한 환경

- .NET 6.0 이상
- Windows (WPF)
- System.Text.Encoding.CodePages 패키지 (EUC-KR 인코딩 지원)

## 세이브 파일 위치

프로그램은 다음 경로에서 자동으로 세이브 파일을 찾습니다:

1. 실행 파일과 같은 폴더의 `SAVEDATA.CDS`
2. `C:\Users\ocean\Desktop\대항해시대3\SAVEDATA.CDS`

또는 "세이브 파일 읽기" 버튼을 눌러서 직접 선택할 수 있습니다.

## 사용 방법

1. 프로그램 실행 시 자동으로 세이브 파일을 로드합니다
2. **미등장 캐릭터 표시**: 체크하면 18세 미만/60세 초과 캐릭터도 표시
3. **이름 검색**: 텍스트 입력 시 실시간으로 이름 필터링
4. **특기 필터**:
   - 체크박스를 체크하면 특기 필터 활성화
   - 특기 선택 (항해술, 운용술, 검술 등 26개)
   - 레벨 선택 (1-9)
   - 해당 특기와 레벨을 가진 캐릭터만 표시

## 데이터 구조

- 캐릭터 시작 오프셋: 0x924A (37,450 바이트)
- 캐릭터 크기: 0x90 (144 바이트)
- 최대 캐릭터 수: 461개
- 인코딩: EUC-KR (코드페이지 51949)

### 주요 오프셋

- 0x00-0x04: 체력, 지력, 무력, 매력, 운
- 0x0A-0x24: 특기 (27개, 인덱스 0은 등장 여부)
- 0x26-0x27: 명성 (uint16, little-endian)
- 0x2E: 소재 (도시 인덱스, 255 = 함대소속)
- 0x32, 0x45: 이름 (20바이트씩, EUC-KR)
- 0x5C: 연령 (signed byte)
- 0x60: 얼굴 인덱스
- 0x70+: 성좌

## 참고 자료

- 지도 기능: https://ellun.tistory.com/368
