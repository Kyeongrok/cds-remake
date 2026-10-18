주인공 능력치 에디터 검증 (Windows / .NET 10)

저장·복원 및 WPF UI 로직을 실제 게임 모듈로 검사한다.
기본 사용자 세이브 위치에는 쓰지 않고, 첫 번째 인자로 지정한 테스트 폴더를 사용한다.
게임 설정은 해당 프로세스의 메모리에서만 바꾸며 설정 저장 함수는 호출하지 않는다.
실제 지도 창을 띄우므로 Windows 데스크톱 세션이 필요하다.
게임 지도 자료가 없으면 기존 게임 코드가 자료를 다운로드할 수 있다.

저장소 루트에서 실행:
dotnet run --project tools/ability-editor-checks/AbilityEditor.Checks.csproj -c Release -- C:\Temp\cds-editor-checks

별도 프로세스로 앞서 저장한 값을 다시 불러오기:
dotnet run --project tools/ability-editor-checks/AbilityEditor.Checks.csproj -c Release -- C:\Temp\cds-editor-checks --verify-only

메뉴와 세 탭을 직접 점검할 수 있도록 창을 남기기:
dotnet run --project tools/ability-editor-checks/AbilityEditor.Checks.csproj -c Release -- C:\Temp\cds-editor-checks --interactive

검증 결과는 지정한 폴더의 checks.log에 기록한다. 실패하면 failure.txt와 종료 코드 1을 반환한다.
--verify-only는 checks.log를 해당 실행의 결과로 갱신한다.
오프닝·음악 다운로드는 검사에서 건너뛰며, GameSave.Save 및 WorldMapWindow.LoadGame을 사용한다.
일반 저장의 BeforeSave / Aging.OnSave 처리도 실행한 뒤 저장 파일 경로만 테스트 폴더로 지정한다.
25세 캐릭터를 사용하므로 노화에 의한 능력치 변화는 없다.
