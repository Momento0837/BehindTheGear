# Pixel Bridge Layer Sprites

Unity 6에서 Aseprite 원본의 첫 프레임을 레이어별 스프라이트로 가져옵니다. 원본 캔버스 크기와 중앙 피벗을 공유하므로 같은 위치에 놓으면 각 그림이 원래 위치에 맞습니다.

## 설치와 사용

1. `PixelBridgeLayerSprites` 폴더를 프로젝트의 `Packages/com.local.pixelbridge-layer-sprites`로 복사합니다. 외부 폴더에 두고 쓰려면 Unity의 **Window → Package Manager → + → Install package from disk**에서 이 폴더의 `package.json`을 선택해도 됩니다.
2. Unity Project 창에서 `Assets` 아래의 `.aseprite` 또는 `.ase` 원본을 우클릭하고 **Pixel Bridge → Import Visible Layer Sprites**를 선택합니다. Aseprite 1.3 이상이 필요합니다. Steam 설치는 자동으로 찾고, 찾지 못하면 실행 파일 선택 창이 열립니다.
3. 원본 옆에 생긴 `.pblayers` 에셋의 삼각형을 펼칩니다. 원하는 Sprite를 SpriteRenderer의 **Sprite** 필드나 UI Image의 **Source Image** 필드로 끌어 놓습니다.

눈 아이콘이 켜진 그림 레이어 중 첫 프레임에 보이는 픽셀이 있는 레이어만 가져옵니다. 숨긴 그룹의 자손과 참조 레이어도 제외합니다. 그룹 안의 스프라이트는 `장비 / 손 / 칼`처럼 그룹 경로를 붙여 표시합니다. 텍스처 필터는 Point이며 기본값은 100 PPU입니다.

원본을 저장하고 Unity가 새로 고치면 그림과 표시 상태가 갱신됩니다. 모든 레이어를 숨기면 결과 목록이 비워집니다. 레이어를 숨기면 해당 Sprite도 결과에서 빠지므로 그 Sprite를 사용 중인 오브젝트를 확인하세요. 팀 저장소에는 원본, `.pblayers`, 두 파일의 `.meta`를 함께 넣으세요. 실행 파일 경로는 각 컴퓨터에서 **Tools → Pixel Bridge → Layer Sprites → Select Aseprite**로 지정할 수 있습니다.
