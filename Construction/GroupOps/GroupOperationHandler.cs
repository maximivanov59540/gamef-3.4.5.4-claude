using System.Collections.Generic;
using UnityEngine;

public class GroupOperationHandler : MonoBehaviour
{
    public static GroupOperationHandler Instance { get; private set; }

    [Header("Ссылки")]
    [SerializeField] private GridSystem _gridSystem;
    [SerializeField] private PlayerInputController _inputController;
    [SerializeField] private BuildingManager _buildingManager;
    [SerializeField] private RoadManager _roadManager;

    private NotificationManager _notificationManager;

    // Пул призраков для предпросмотра копирования
    private readonly List<GameObject> _ghostPool = new();
    private int _ghostPoolIndex = 0;

    private struct GroupOffset
    {
        public BuildingData data;
        public Vector2Int offset;
        public float yRotationDelta;
        public bool isBlueprint;
    }

    private struct RoadOffset
    {
        public RoadData roadData;
        public Vector2Int offset;
    }
    private readonly List<GroupOffset> _currentGroupOffsets_Copy = new();
    private readonly List<RoadOffset> _currentRoadOffsets_Copy = new();
    private readonly List<LiftedBuildingData> _liftedBuildingData = new();
    private readonly List<RoadOffset> _liftedRoadOffsets = new();
    private struct LiftedBuildingData
    {
        public GameObject gameObject;
        public GroupOffset offset;
        public Vector2Int originalPosition;
        public float originalRotation;
    }

    private readonly List<GroupOffset> _currentGroupOffsets = new();
    private Vector2Int _anchorGridPos;
    private float _anchorRotation;
    private float _currentGroupRotation = 0f;
    private bool _canPlaceGroup = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        _notificationManager = FindFirstObjectByType<NotificationManager>();
        if (_roadManager == null) _roadManager = FindFirstObjectByType<RoadManager>();
    }

    // --- Вспомогательные математики ---

    private static Vector2Int RotateVector(Vector2Int v, float angle)
    {
        int x = v.x, y = v.y;
        if (Mathf.Abs(angle - 90f) < 1f)  return new Vector2Int(-y,  x);
        if (Mathf.Abs(angle - 180f) < 1f) return new Vector2Int(-x, -y);
        if (Mathf.Abs(angle - 270f) < 1f) return new Vector2Int( y, -x);
        return v;
    }

    private static Vector2Int GetRotatedSize(Vector2Int size, float angle)
    {
        if (Mathf.Abs(angle - 90f) < 1f || Mathf.Abs(angle - 270f) < 1f)
            return new Vector2Int(size.y, size.x);
        return size;
    }

    // Собирает все дороги, которые находятся под зданиями в выделении
    private HashSet<Vector2Int> CollectRoadsUnderBuildings(HashSet<BuildingIdentity> buildings)
    {
        var roadPositions = new HashSet<Vector2Int>();

        foreach (var building in buildings)
        {
            if (building == null) continue;

            Vector2Int rootPos = building.rootGridPosition;
            Vector2Int size = GetRotatedSize(building.buildingData.size, building.yRotation);

            // Проверяем все клетки, занятые зданием
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    Vector2Int checkPos = new Vector2Int(rootPos.x + x, rootPos.y + y);
                    RoadTile road = _gridSystem.GetRoadTileAt(checkPos.x, checkPos.y);
                    if (road != null && road.roadData != null)
                    {
                        roadPositions.Add(checkPos);
                    }
                }
            }
        }

        return roadPositions;
    }

    // --------- Массовое копирование (D/E/F) ---------

    public void StartMassCopy(HashSet<BuildingIdentity> selection, List<Vector2Int> roadCells)
    {
        _currentGroupOffsets_Copy.Clear();
        _currentRoadOffsets_Copy.Clear();
        _currentGroupRotation = 0f;

        BuildingIdentity anchorId = null;
        int minPosSum = int.MaxValue;
        foreach (var id in selection)
        {
            int sum = id.rootGridPosition.x + id.rootGridPosition.y;
            if (sum < minPosSum) { minPosSum = sum; anchorId = id; }
        }
        if (anchorId == null) return;
        _anchorGridPos = anchorId.rootGridPosition;
        _anchorRotation = anchorId.yRotation;

        foreach (var id in selection)
        {
            _currentGroupOffsets_Copy.Add(new GroupOffset
            {
                data = id.buildingData,
                offset = id.rootGridPosition - _anchorGridPos,
                yRotationDelta = id.yRotation - _anchorRotation,
                isBlueprint = id.isBlueprint
            });
        }
        var allRoadPositions = new HashSet<Vector2Int>(roadCells);
        allRoadPositions.UnionWith(CollectRoadsUnderBuildings(selection));

        // Собираем дороги под зданиями
foreach (var roadPos in allRoadPositions)
        {
            RoadTile roadTile = _gridSystem.GetRoadTileAt(roadPos.x, roadPos.y);
            if (roadTile != null && roadTile.roadData != null)
            {
                _currentRoadOffsets_Copy.Add(new RoadOffset
                {
                    roadData = roadTile.roadData,
                    offset = roadPos - _anchorGridPos
                });
            }
        }

        _inputController.SetMode(InputMode.GroupCopying);
    }

    public void UpdateGroupPreview(Vector2Int mouseGridPos)
    {
        _ghostPoolIndex = 0;
        _canPlaceGroup = true;
        float cellSize = _gridSystem.GetCellSize();

        foreach (var entry in _currentGroupOffsets_Copy)
        {
            Vector2Int rotatedOffset = RotateVector(entry.offset, _currentGroupRotation);
            Vector2Int finalPos = mouseGridPos + rotatedOffset;
            float finalRot = (_currentGroupRotation + entry.yRotationDelta) % 360f;
            Vector2Int finalSize = GetRotatedSize(entry.data.size, finalRot);

            GameObject ghost = GetGhostFromPool(entry.data);
            ghost.transform.rotation = Quaternion.Euler(0, finalRot, 0);

            Vector3 worldPos = _gridSystem.GetWorldPosition(finalPos.x, finalPos.y);
            worldPos.x += (finalSize.x * cellSize) / 2f;
            worldPos.z += (finalSize.y * cellSize) / 2f;
            ghost.transform.position = worldPos;

            bool canPlace = _gridSystem.CanBuildAt(finalPos, finalSize);

            var visuals = ghost.GetComponent<BuildingVisuals>();
            if (visuals != null)
            {
                visuals.SetState(entry.isBlueprint ? VisualState.Blueprint : VisualState.Ghost, canPlace);
            }

            if (!canPlace) _canPlaceGroup = false;
        }
        foreach (var roadEntry in _currentRoadOffsets_Copy)
    {
        Vector2Int rotatedOffset = RotateVector(roadEntry.offset, _currentGroupRotation);
        Vector2Int finalPos = mouseGridPos + rotatedOffset;

        // Получаем призрак (используем префаб из RoadData)
        GameObject ghost = GetGhostFromPool(roadEntry.roadData);

        // Позиционируем призрак дороги
        Vector3 worldPos = _gridSystem.GetWorldPosition(finalPos.x, finalPos.y);
        worldPos.x += cellSize / 2f;
        worldPos.z += cellSize / 2f;
        worldPos.y += 0.01f;
        ghost.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(90, 0, 0));

        // Проверяем, можно ли тут строить
        bool canPlace = !_gridSystem.IsCellOccupied(finalPos.x, finalPos.y);

        // Красим (используем BuildingVisuals, если он есть)
        var visuals = ghost.GetComponent<BuildingVisuals>();
        if (visuals != null)
        {
            visuals.SetState(VisualState.Ghost, canPlace);
        }
        // (Если нет BuildingVisuals, можешь добавить сюда покраску материала,
        // но на префабах дорог он должен быть для единообразия)

        if (!canPlace) _canPlaceGroup = false;
    }

        HideUnusedGhosts();
    }

public void RotateGroupPreview()
    {
        _currentGroupRotation = (_currentGroupRotation + 90f) % 360f;
    }

    public void ExecutePlaceGroupCopy()
    {
        if (!_canPlaceGroup)
        {
            _notificationManager?.ShowNotification("Место занято!");
            return; // "Выходим", "не" "строя" "частично"
        }
        Vector2Int anchorPos = GridSystem.MouseGridPosition;
        if (anchorPos.x == -1) return;

        int successBuildings = 0;
        int successRoads = 0;
        bool blueprintMode = BlueprintManager.IsActive;

        // Сначала копируем дороги
        foreach (var roadEntry in _currentRoadOffsets_Copy)
        {
            Vector2Int rotatedOffset = RotateVector(roadEntry.offset, _currentGroupRotation);
            Vector2Int finalPos = anchorPos + rotatedOffset;

            // Проверяем, можно ли поставить дорогу
            if (_gridSystem.GetRoadTileAt(finalPos.x, finalPos.y) == null &&
                _gridSystem.GetBuildingIdentityAt(finalPos.x, finalPos.y) == null)
            {
                _roadManager.PlaceRoad(finalPos, roadEntry.roadData);
                successRoads++;
            }
        }

        // Потом копируем здания
        foreach (var entry in _currentGroupOffsets_Copy)
        {
            Vector2Int rotatedOffset = RotateVector(entry.offset, _currentGroupRotation);
            Vector2Int finalPos = anchorPos + rotatedOffset;
            float finalRot = (_currentGroupRotation + entry.yRotationDelta) % 360f;

            Vector2Int finalSize = GetRotatedSize(entry.data.size, finalRot);
            if (!_gridSystem.CanBuildAt(finalPos, finalSize))
                continue;

            if (_buildingManager.PlaceBuildingFromOrder(entry.data, finalPos, finalRot, blueprintMode))
                successBuildings++;
            else
                break; // закончились ресурсы
        }

        if (successBuildings > 0 || successRoads > 0)
        {
            string msg = "";
            if (successBuildings > 0) msg += $"Скопировано {successBuildings} зданий";
            if (successRoads > 0)
            {
                if (msg != "") msg += " и ";
                msg += $"{successRoads} дорог";
            }
            msg += ".";
            _notificationManager?.ShowNotification(msg);
        }

    }

    // НОВЫЙ "ТИХИЙ" МЕТОД ОЧИСТКИ (не меняет режим)
    private void QuietCancel()
    {
        if (_liftedBuildingData.Count > 0)
        {
            // Возвращаем дороги обратно
            foreach (var roadEntry in _liftedRoadOffsets)
            {
                _roadManager.PlaceRoad(_anchorGridPos + roadEntry.offset, roadEntry.roadData);
            }

            // "Используем" "новый" "объединенный" "список"
            foreach(var liftedData in _liftedBuildingData)
            {
                GameObject go = liftedData.gameObject;
                if (go == null) continue;

                var id = go.GetComponent<BuildingIdentity>();
                if (id == null) continue;

                Vector2Int origPos = liftedData.originalPosition;
                float origRot = liftedData.originalRotation;
                Vector2Int origSize = GetRotatedSize(id.buildingData.size, origRot);

                id.rootGridPosition = origPos;
                id.yRotation = origRot;

                float cellSize = _gridSystem.GetCellSize();
                Vector3 worldPos = _gridSystem.GetWorldPosition(origPos.x, origPos.y);
                worldPos.x += (origSize.x * cellSize) / 2f;
                worldPos.z += (origSize.y * cellSize) / 2f;
                go.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0, origRot, 0));

                _gridSystem.OccupyCells(id, origSize);

                _buildingManager.SetBuildingVisuals(go, id.isBlueprint ? VisualState.Blueprint : VisualState.Real, true);
                if (!id.isBlueprint)
                {
                    BuildOrchestrator.Instance?.PauseProduction(go, false);
                }
            }
        }
        else
        {
            // Отмена призраков (копирование)
            _ghostPoolIndex = 0;
            HideUnusedGhosts();
        }

        ClearAllLists();
    }

    /// <summary>
    /// СТАРЫЙ МЕТОД, ВЫЗЫВАЕМЫЙ ИЗ "ОРКЕСТРАТОРА".
    /// Теперь он "тихий" и только чистит, не меняя режим.
    /// </summary>
    public void CancelGroupOperation()
    {
        QuietCancel();
    }

    /// <summary>
    /// НОВЫЙ МЕТОД, ВЫЗЫВАЕМЫЙ ИЗ InputStates (State_GroupMoving/Copying).
    /// Этот метод чистит И выходит в Mode.None.
    /// </summary>
    public void CancelAndExitMode()
    {
        QuietCancel();
        _inputController.SetMode(InputMode.None);
    }

    // --------- Массовое перемещение (C/D) ---------

    public void StartMassMove(HashSet<BuildingIdentity> selection, List<Vector2Int> roadCells)
    {
        ClearAllLists();

        // Якорь
        BuildingIdentity anchorId = null;
        int minPosSum = int.MaxValue;
        foreach (var id in selection)
        {
            int sum = id.rootGridPosition.x + id.rootGridPosition.y;
            if (sum < minPosSum) { minPosSum = sum; anchorId = id; }
        }
        if (anchorId == null) return;

        _anchorGridPos = anchorId.rootGridPosition;
        _anchorRotation = anchorId.yRotation;
        var allRoadPositions = new HashSet<Vector2Int>(roadCells);
        
        // 2. Добавляем дороги ПОД зданиями
        allRoadPositions.UnionWith(CollectRoadsUnderBuildings(selection));

        // Сначала собираем дороги под зданиями
        foreach (var roadPos in allRoadPositions)
        {
            RoadTile roadTile = _gridSystem.GetRoadTileAt(roadPos.x, roadPos.y);
            if (roadTile != null && roadTile.roadData != null)
            {
                _liftedRoadOffsets.Add(new RoadOffset
                {
                    roadData = roadTile.roadData,
                    offset = roadPos - _anchorGridPos
                });
                // Удаляем дорогу с карты
                _roadManager.RemoveRoad(roadPos);
            }
        }

        foreach (var id in selection)
        {
            GameObject lifted = _gridSystem.PickUpBuilding(id.rootGridPosition.x, id.rootGridPosition.y);

            // "Главный" "фикс": "если" "здание" "нельзя" "поднять" - "ПРОПУСКАЕМ"
            if (lifted == null)
            {
                continue; // "Не" "добавляем" "в" "список"
            }

            // "Здание" "поднято", "создаем" "ОДИН" "объект" "данных"
            _liftedBuildingData.Add(new LiftedBuildingData
            {
                gameObject = lifted,
                originalPosition = id.rootGridPosition,
                originalRotation = id.yRotation,
                offset = new GroupOffset
                {
                    data = id.buildingData,
                    offset = id.rootGridPosition - _anchorGridPos,
                    yRotationDelta = id.yRotation - _anchorRotation,
                    isBlueprint = id.isBlueprint
                }
            });

            _buildingManager.SetBuildingVisuals(lifted, VisualState.Ghost, true);
            BuildOrchestrator.Instance?.PauseProduction(lifted, true);
        }
        _inputController.SetMode(InputMode.GroupMoving);
    }

    public void UpdateGroupMovePreview(Vector2Int mouseGridPos)
    {
        _canPlaceGroup = true;

        float cellSize = _gridSystem.GetCellSize();

        foreach (var liftedData in _liftedBuildingData)
        {
            GameObject go = liftedData.gameObject;
            GroupOffset entry = liftedData.offset;

            Vector2Int rotatedOffset = RotateVector(entry.offset, _currentGroupRotation);
            Vector2Int finalPos = mouseGridPos + rotatedOffset;
            float finalRot = (_currentGroupRotation + entry.yRotationDelta) % 360f;
            Vector2Int finalSize = GetRotatedSize(entry.data.size, finalRot);

            go.transform.rotation = Quaternion.Euler(0, finalRot, 0);

            Vector3 worldPos = _gridSystem.GetWorldPosition(finalPos.x, finalPos.y);
            worldPos.x += (finalSize.x * cellSize) / 2f;
            worldPos.z += (finalSize.y * cellSize) / 2f;
            go.transform.position = worldPos;

            bool canPlace = _gridSystem.CanBuildAt(finalPos, finalSize);
            _buildingManager.CheckPlacementValidity(go, entry.data, finalPos);
            if (!canPlace) _canPlaceGroup = false;
        }
    }

    public void PlaceGroupMove()
    {
        if (!_canPlaceGroup)
        {
            _notificationManager?.ShowNotification("Место занято!");
            return;
        }

        Vector2Int anchorPos = GridSystem.MouseGridPosition;
        if (anchorPos.x == -1) return;

        // Сначала размещаем дороги
        foreach (var roadEntry in _liftedRoadOffsets)
        {
            Vector2Int rotatedOffset = RotateVector(roadEntry.offset, _currentGroupRotation);
            Vector2Int finalPos = anchorPos + rotatedOffset;
            _roadManager.PlaceRoad(finalPos, roadEntry.roadData);
        }

        // Потом размещаем здания
        foreach (var liftedData in _liftedBuildingData)
        {
            GameObject go = liftedData.gameObject;
            GroupOffset entry = liftedData.offset;
            var id = go.GetComponent<BuildingIdentity>();
            bool wasBlueprint = id.isBlueprint;

            Vector2Int rotatedOffset = RotateVector(entry.offset, _currentGroupRotation);
            Vector2Int finalPos = anchorPos + rotatedOffset;
            float finalRot = (_currentGroupRotation + entry.yRotationDelta) % 360f;
            Vector2Int finalSize = GetRotatedSize(entry.data.size, finalRot);

            id.rootGridPosition = finalPos;
            id.yRotation = finalRot;

            _gridSystem.OccupyCells(id, finalSize);

            _buildingManager.SetBuildingVisuals(go, wasBlueprint ? VisualState.Blueprint : VisualState.Real, true);
            if (!wasBlueprint)
            {
                BuildOrchestrator.Instance?.PauseProduction(go, false);
            }
        }

        _notificationManager?.ShowNotification("Группа перемещена.");

        ClearAllLists();
        _inputController.SetMode(InputMode.None);
    }

    // --------- Пул и очистка ---------

    private GameObject GetGhostFromPool(BuildingData data)
{
    GameObject ghost;
    if (_ghostPoolIndex < _ghostPool.Count)
    {
        ghost = _ghostPool[_ghostPoolIndex];
    }
    else
    {
        ghost = Instantiate(data.buildingPrefab, transform);
        ghost.layer = LayerMask.NameToLayer("Ghost");
        ghost.tag = "Untagged"; 

        var producer = ghost.GetComponent<ResourceProducer>();
        if (producer != null) producer.enabled = false;
        var identity = ghost.GetComponent<BuildingIdentity>();
        if (identity != null) identity.enabled = false;
        
        // --- УМНАЯ НАСТРОЙКА ФИЗИКИ ---
        bool hasConcaveMesh = false;
        var colliders = ghost.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            if (col is MeshCollider meshCol && !meshCol.convex)
            {
                hasConcaveMesh = true;
                col.enabled = false; // Выключаем вогнутые коллайдеры
            }
            else
            {
                col.isTrigger = true; // Остальные (Box, Sphere, Convex) делаем триггерами
            }
        }

        // Добавляем Rigidbody, ТОЛЬКО если нет вогнутых коллайдеров
        // (Это нужно для GhostBuildingCollider в режиме ПЕРЕМЕЩЕНИЯ)
        if (!hasConcaveMesh)
        {
            var rb = ghost.GetComponent<Rigidbody>();
            if (rb == null) rb = ghost.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
        // --- КОНЕЦ УМНОЙ НАСТРОЙКИ ---
        
        _ghostPool.Add(ghost);
    }

    ghost.SetActive(true);
    _ghostPoolIndex++;
    return ghost;
}
    private GameObject GetGhostFromPool(RoadData data)
{
    GameObject ghost;
    if (_ghostPoolIndex < _ghostPool.Count)
    {
        ghost = _ghostPool[_ghostPoolIndex];
    }
    else
    {
        // --- Используем префаб дороги ---
        ghost = Instantiate(data.roadPrefab, transform); 
        
        // --- ИСПРАВЛЕННАЯ ЛОГИКА ДЛЯ ДОРОГ ---
        ghost.layer = LayerMask.NameToLayer("Ghost");
        ghost.tag = "Untagged"; 
        
        // Призракам дорог не нужна физика, они проверяются
        // только по сетке (_gridSystem.IsCellOccupied).
        // Мы просто ВЫКЛЮЧАЕМ коллайдеры, чтобы они ничему не мешали.
        var colliders = ghost.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            col.enabled = false; 
        }
        
        // Мы НЕ добавляем Rigidbody и НЕ ставим isTrigger = true
        
        _ghostPool.Add(ghost);
    }

    ghost.SetActive(true);
    _ghostPoolIndex++;
    return ghost;
}

    private void HideUnusedGhosts()
    {
        for (int i = _ghostPoolIndex; i < _ghostPool.Count; i++)
            _ghostPool[i].SetActive(false);
    }

    private void ClearAllLists()
    {
        _ghostPoolIndex = 0;
        _currentGroupOffsets_Copy.Clear();
        _currentRoadOffsets_Copy.Clear();
        _liftedBuildingData.Clear();
        _liftedRoadOffsets.Clear();
        _currentGroupRotation = 0f;
    }
    private void OnDestroy()
    {
        foreach (var ghost in _ghostPool)
        {
            if (ghost != null)
            {
                Destroy(ghost);
            }
        }
        _ghostPool.Clear();
    }
}