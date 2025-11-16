using UnityEngine;

/// <summary>
/// Управляет маршрутизацией ресурсов для производственного здания.
/// Определяет, КУДА отвозить Output и ОТКУДА брать Input.
/// 
/// Использование:
/// - Добавьте на производственное здание
/// - Настройте маршруты в Inspector:
///   * outputDestinationTransform - куда везти продукцию (или null для автопоиска склада)
///   * inputSourceTransform - откуда брать сырьё (или null для автопоиска склада)
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
public class BuildingResourceRouting : MonoBehaviour
{
    [Header("Output Routing (куда отвозить продукцию)")]
    [Tooltip("Целевое здание для Output. Оставьте пустым для автопоиска ближайшего склада")]
    public Transform outputDestinationTransform;
    
    [Header("Input Routing (откуда брать сырьё)")]
    [Tooltip("Источник для Input. Оставьте пустым для автопоиска ближайшего склада")]
    public Transform inputSourceTransform;
    
    [Header("Дебаг (только для чтения)")]
    [SerializeField] private string _outputDestinationName = "не настроен";
    [SerializeField] private string _inputSourceName = "не настроен";
    
    [Header("Автообновление")]
    [Tooltip("Интервал повторной проверки маршрутов (сек), если они не настроены")]
    [SerializeField] private float _retryInterval = 5.0f;

    [Header("Приоритеты Input (только для чтения)")]
    [Tooltip("Предпочитать прямые поставки от производителей вместо склада")]
    [SerializeField] private bool _preferDirectSupply = true;

    // Кэшированные интерфейсы
    public IResourceReceiver outputDestination { get; private set; }
    public IResourceProvider inputSource { get; private set; }
    private BuildingIdentity _identity;
    private float _retryTimer = 0f;

    // Кэшированные системы для поиска путей
    private GridSystem _gridSystem;
    private RoadManager _roadManager;
    
    void Awake()
    {
        _identity = GetComponent<BuildingIdentity>();
        
        if (_identity == null)
        {
            Debug.LogError($"[BuildingResourceRouting] {gameObject.name} не имеет BuildingIdentity!");
        }
    }
    
    void Start()
    {
        // Инициализируем системы для поиска путей
        _gridSystem = FindFirstObjectByType<GridSystem>();
        _roadManager = RoadManager.Instance;

        if (_gridSystem == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: GridSystem не найден!");
        }
        if (_roadManager == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: RoadManager не найден!");
        }

        RefreshRoutes();
    }
    void Update()
    {
        // ✅ АВТООБНОВЛЕНИЕ: Если маршруты не настроены, повторяем проверку каждые N секунд
        // Это решает проблему, когда здание строится ДО склада
        if (!IsConfigured())
        {
            _retryTimer += Time.deltaTime;
            if (_retryTimer >= _retryInterval)
            {
                _retryTimer = 0f;
                Debug.Log($"[Routing] {gameObject.name}: Маршруты не настроены, повторная проверка...");
                RefreshRoutes();
                // Уведомляем ResourceProducer об изменении
                var producer = GetComponent<ResourceProducer>();
                if (producer != null)
                {
                    producer.RefreshWarehouseAccess();
                }
            }
        }
    }
    /// <summary>
    /// Обновляет маршруты (вызывать при изменении зданий на карте)
    /// </summary>
    public void RefreshRoutes()
    {
        // === OUTPUT DESTINATION ===
        if (outputDestinationTransform != null)
        {
            // Используем указанное здание
            outputDestination = outputDestinationTransform.GetComponent<IResourceReceiver>();
            
            if (outputDestination == null)
            {
                Debug.LogWarning($"[Routing] {gameObject.name}: {outputDestinationTransform.name} не реализует IResourceReceiver!");
                _outputDestinationName = $"{outputDestinationTransform.name} (ОШИБКА)";
            }
            else
            {
                _outputDestinationName = outputDestinationTransform.name;
                Debug.Log($"[Routing] {gameObject.name}: Output → {outputDestinationTransform.name}");
            }
        }
        else
        {
            // Автопоиск ближайшего склада
            outputDestination = FindNearestWarehouse();
            
            if (outputDestination != null)
            {
                _outputDestinationName = $"Склад (авто) на {outputDestination.GetGridPosition()}";
                Debug.Log($"[Routing] {gameObject.name}: Output → автопоиск склада на {outputDestination.GetGridPosition()}");
            }
            else
            {
                _outputDestinationName = "НЕ НАЙДЕН!";
                Debug.LogWarning($"[Routing] {gameObject.name}: Output получатель НЕ НАЙДЕН! Постройте склад.");
            }
        }
        
        // === INPUT SOURCE ===
        if (inputSourceTransform != null)
        {
            // Используем указанное здание (ручная настройка)
            inputSource = inputSourceTransform.GetComponent<IResourceProvider>();

            if (inputSource == null)
            {
                Debug.LogWarning($"[Routing] {gameObject.name}: {inputSourceTransform.name} не реализует IResourceProvider!");
                _inputSourceName = $"{inputSourceTransform.name} (ОШИБКА)";
            }
            else
            {
                _inputSourceName = inputSourceTransform.name;
                Debug.Log($"[Routing] {gameObject.name}: Input ← {inputSourceTransform.name}");
            }
        }
        else
        {
            // ✅ НОВАЯ СИСТЕМА ПРИОРИТЕТОВ: производитель > склад
            if (_preferDirectSupply)
            {
                // Приоритет 1: Ищем производителя нужного ресурса
                inputSource = FindNearestProducerForMyNeeds();

                if (inputSource != null)
                {
                    // Нашли производителя!
                    if (inputSource is MonoBehaviour mb)
                    {
                        _inputSourceName = $"{mb.name} (производитель)";
                        Debug.Log($"[Routing] {gameObject.name}: Input ← производитель {mb.name}");
                    }
                }
            }

            // Приоритет 2: Если производителя нет → ищем склад
            if (inputSource == null)
            {
                inputSource = FindNearestWarehouse();

                if (inputSource != null)
                {
                    _inputSourceName = $"Склад (авто) на {inputSource.GetGridPosition()}";
                    Debug.Log($"[Routing] {gameObject.name}: Input ← автопоиск склада на {inputSource.GetGridPosition()}");
                }
                else
                {
                    _inputSourceName = "НЕ НАЙДЕН!";
                    Debug.LogWarning($"[Routing] {gameObject.name}: Input источник НЕ НАЙДЕН! Постройте склад или производителя.");
                }
            }
        }
    }
    
    /// <summary>
    /// ✅ НОВОЕ: Ищет ближайшего производителя нужного ресурса (с проверкой дорог)
    /// </summary>
    private IResourceProvider FindNearestProducerForMyNeeds()
    {
        // 1. Определяем, какой ресурс нам нужен
        var inputInv = GetComponent<BuildingInputInventory>();
        if (inputInv == null || inputInv.requiredResources == null || inputInv.requiredResources.Count == 0)
        {
            // Здание не требует Input
            return null;
        }

        // Берём первый требуемый ресурс (если их несколько, можно расширить логику)
        ResourceType neededType = inputInv.requiredResources[0].resourceType;

        Debug.Log($"[Routing] {gameObject.name}: Ищу производителя {neededType}...");

        // 2. Находим все здания с BuildingOutputInventory
        BuildingOutputInventory[] allOutputs = FindObjectsByType<BuildingOutputInventory>(FindObjectsSortMode.None);

        if (allOutputs.Length == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Не найдено ни одного производителя на карте");
            return null;
        }

        // 3. Фильтруем по типу ресурса
        var matchingProducers = new System.Collections.Generic.List<BuildingOutputInventory>();

        foreach (var output in allOutputs)
        {
            // Проверяем, что это не мы сами
            if (output.gameObject == gameObject)
                continue;

            // Проверяем тип ресурса
            if (output.outputResource.resourceType == neededType)
            {
                matchingProducers.Add(output);
            }
        }

        if (matchingProducers.Count == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Не найдено производителей {neededType}");
            return null;
        }

        Debug.Log($"[Routing] {gameObject.name}: Найдено {matchingProducers.Count} производителей {neededType}. Проверяю доступность по дорогам...");

        // 4. Проверяем доступность по дорогам и находим ближайшего
        if (_gridSystem == null || _roadManager == null || _identity == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Системы не инициализированы, выбираю ближайшего по расстоянию");
            return FindNearestByDistance(matchingProducers);
        }

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Граф дорог пуст, выбираю ближайшего по расстоянию");
            return FindNearestByDistance(matchingProducers);
        }

        // Находим наши точки доступа к дорогам
        var myAccessPoints = LogisticsPathfinder.FindAllRoadAccess(_identity.rootGridPosition, _gridSystem, roadGraph);

        if (myAccessPoints.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: У меня нет доступа к дорогам!");
            return null;
        }

        // Рассчитываем расстояния от нас до всех точек дорог
        var distancesFromMe = LogisticsPathfinder.Distances_BFS_Multi(myAccessPoints, 1000, roadGraph);

        // Ищем ближайшего доступного производителя
        IResourceProvider nearestProducer = null;
        int minRoadDistance = int.MaxValue;

        foreach (var producer in matchingProducers)
        {
            var producerIdentity = producer.GetComponent<BuildingIdentity>();
            if (producerIdentity == null)
                continue;

            var producerAccessPoints = LogisticsPathfinder.FindAllRoadAccess(producerIdentity.rootGridPosition, _gridSystem, roadGraph);

            foreach (var accessPoint in producerAccessPoints)
            {
                if (distancesFromMe.TryGetValue(accessPoint, out int dist) && dist < minRoadDistance)
                {
                    minRoadDistance = dist;
                    nearestProducer = producer;
                }
            }
        }

        if (nearestProducer != null)
        {
            if (nearestProducer is MonoBehaviour mb)
            {
                Debug.Log($"[Routing] {gameObject.name}: Нашёл производителя {neededType}: {mb.name} (дистанция по дороге: {minRoadDistance})");
            }
        }
        else
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Производители {neededType} найдены, но нет дороги к ним!");
        }

        return nearestProducer;
    }

    /// <summary>
    /// Вспомогательный метод: находит ближайшего производителя по прямому расстоянию
    /// </summary>
    private IResourceProvider FindNearestByDistance(System.Collections.Generic.List<BuildingOutputInventory> producers)
    {
        BuildingOutputInventory nearest = null;
        float minDistance = float.MaxValue;

        foreach (var producer in producers)
        {
            float dist = Vector3.Distance(transform.position, producer.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = producer;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Ищет ближайший склад (возвращает как IResourceProvider и IResourceReceiver одновременно)
    /// </summary>
    private Warehouse FindNearestWarehouse()
    {
        Warehouse[] warehouses = FindObjectsByType<Warehouse>(FindObjectsSortMode.None);

        if (warehouses.Length == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: На карте нет ни одного склада!");
            return null;
        }

        Warehouse nearest = null;
        float minDistance = float.MaxValue;

        foreach (var wh in warehouses)
        {
            float dist = Vector3.Distance(transform.position, wh.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = wh;
            }
        }

        return nearest;
    }
    
    /// <summary>
    /// Устанавливает конкретный маршрут для Output (для программной настройки цепочек)
    /// </summary>
    public void SetOutputDestination(Transform destination)
    {
        outputDestinationTransform = destination;
        RefreshRoutes();
    }
    
    /// <summary>
    /// Устанавливает конкретный источник для Input (для программной настройки цепочек)
    /// </summary>
    public void SetInputSource(Transform source)
    {
        inputSourceTransform = source;
        RefreshRoutes();
    }
    
    /// <summary>
    /// Проверяет, настроены ли маршруты
    /// </summary>
    public bool IsConfigured()
    {
        // Проверяем Output (обязательно для всех зданий)
        if (outputDestination == null)
            return false;
        // ✅ ИСПРАВЛЕНИЕ: Проверяем Input только если здание требует сырьё
        var inputInv = GetComponent<BuildingInputInventory>();
        if (inputInv != null && inputInv.requiredResources != null && inputInv.requiredResources.Count > 0)
        {
            // Здание требует Input - проверяем, что источник настроен
            return inputSource != null;
        }
        // Здание не требует Input (например, лесопилка) - только Output важен
        return true;
    }
    
    /// <summary>
    /// Проверяет, настроен ли Output
    /// </summary>
    public bool HasOutputDestination()
    {
        return outputDestination != null;
    }
    
    /// <summary>
    /// Проверяет, настроен ли Input
    /// </summary>
    public bool HasInputSource()
    {
        return inputSource != null;
    }
    
    // === ДЕБАГ ===
    
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        
        // Рисуем линии от здания к Output destination
        if (outputDestination != null)
        {
            Gizmos.color = Color.green;
            Vector3 start = transform.position + Vector3.up * 2f;
            
            // Если outputDestination - MonoBehaviour, берём его Transform
            if (outputDestination is MonoBehaviour mb)
            {
                Vector3 end = mb.transform.position + Vector3.up * 2f;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(end, 0.5f);
            }
        }
        
        // Рисуем линии от Input source к зданию
        if (inputSource != null)
        {
            Gizmos.color = Color.blue;
            Vector3 end = transform.position + Vector3.up * 2f;
            
            // Если inputSource - MonoBehaviour, берём его Transform
            if (inputSource is MonoBehaviour mb)
            {
                Vector3 start = mb.transform.position + Vector3.up * 2f;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(start, 0.5f);
            }
        }
    }
}