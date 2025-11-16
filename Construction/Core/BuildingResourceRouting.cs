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
    /// ✅ БАЛАНСИРОВКА: Учитывает нагрузку на производителей (сколько потребителей уже подключены)
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
            Debug.LogWarning($"[Routing] {gameObject.name}: Системы не инициализированы, выбираю с балансировкой");
            return FindBalancedProducerByDistance(matchingProducers);
        }

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Граф дорог пуст, выбираю с балансировкой");
            return FindBalancedProducerByDistance(matchingProducers);
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

        // ✅ НОВАЯ ЛОГИКА БАЛАНСИРОВКИ:
        // Собираем информацию о каждом производителе: расстояние + нагрузка
        var producerInfo = new System.Collections.Generic.List<(IResourceProvider provider, int distance, int consumerCount)>();

        foreach (var producer in matchingProducers)
        {
            var producerIdentity = producer.GetComponent<BuildingIdentity>();
            if (producerIdentity == null)
                continue;

            var producerAccessPoints = LogisticsPathfinder.FindAllRoadAccess(producerIdentity.rootGridPosition, _gridSystem, roadGraph);

            // Находим минимальное расстояние до этого производителя
            int minDistToProducer = int.MaxValue;
            foreach (var accessPoint in producerAccessPoints)
            {
                if (distancesFromMe.TryGetValue(accessPoint, out int dist) && dist < minDistToProducer)
                {
                    minDistToProducer = dist;
                }
            }

            // Если производитель недостижим - пропускаем
            if (minDistToProducer == int.MaxValue)
                continue;

            // Подсчитываем нагрузку (сколько потребителей уже подключены к этому производителю)
            int consumerCount = CountConsumersForProducer(producer);

            producerInfo.Add((producer, minDistToProducer, consumerCount));

            Debug.Log($"[Routing] {gameObject.name}: Производитель {producer.name} - дистанция: {minDistToProducer}, потребителей: {consumerCount}");
        }

        if (producerInfo.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Производители {neededType} найдены, но нет дороги к ним!");
            return null;
        }

        // ✅ ВЫБОР С БАЛАНСИРОВКОЙ:
        // Сортируем: сначала по нагрузке (меньше = лучше), затем по расстоянию (ближе = лучше)
        producerInfo.Sort((a, b) =>
        {
            // Приоритет 1: Меньше нагрузка
            int loadComparison = a.consumerCount.CompareTo(b.consumerCount);
            if (loadComparison != 0)
                return loadComparison;

            // Приоритет 2: Меньше расстояние
            return a.distance.CompareTo(b.distance);
        });

        var bestProducer = producerInfo[0];

        if (bestProducer.provider is MonoBehaviour mb)
        {
            Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН производитель {neededType}: {mb.name} (дистанция: {bestProducer.distance}, нагрузка: {bestProducer.consumerCount} потребителей)");
        }

        return bestProducer.provider;
    }

    /// <summary>
    /// ✅ НОВОЕ: Подсчитывает, сколько потребителей уже используют данного производителя как inputSource
    /// </summary>
    private int CountConsumersForProducer(BuildingOutputInventory producer)
    {
        int count = 0;

        // Находим все здания с маршрутизацией
        BuildingResourceRouting[] allRoutings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);

        foreach (var routing in allRoutings)
        {
            // Пропускаем себя
            if (routing == this)
                continue;

            // Проверяем, использует ли это здание нашего производителя как источник Input
            if (ReferenceEquals(routing.inputSource, producer))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// ✅ НОВОЕ: Выбирает производителя с балансировкой по прямому расстоянию (fallback)
    /// </summary>
    private IResourceProvider FindBalancedProducerByDistance(System.Collections.Generic.List<BuildingOutputInventory> producers)
    {
        // Собираем информацию: производитель + расстояние + нагрузка
        var producerInfo = new System.Collections.Generic.List<(IResourceProvider provider, float distance, int consumerCount)>();

        foreach (var producer in producers)
        {
            float dist = Vector3.Distance(transform.position, producer.transform.position);
            int consumerCount = CountConsumersForProducer(producer);

            producerInfo.Add((producer, dist, consumerCount));
        }

        // Сортируем: сначала по нагрузке, затем по расстоянию
        producerInfo.Sort((a, b) =>
        {
            int loadComparison = a.consumerCount.CompareTo(b.consumerCount);
            if (loadComparison != 0)
                return loadComparison;

            return a.distance.CompareTo(b.distance);
        });

        if (producerInfo.Count > 0)
        {
            var best = producerInfo[0];
            if (best.provider is MonoBehaviour mb)
            {
                Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН (по прямой) {mb.name} - расстояние: {best.distance:F1}, нагрузка: {best.consumerCount}");
            }
            return best.provider;
        }

        return null;
    }


    /// <summary>
    /// ✅ ОБНОВЛЕНО: Ищет ближайший склад с проверкой дорог и балансировкой нагрузки
    /// </summary>
    private Warehouse FindNearestWarehouse()
    {
        Warehouse[] warehouses = FindObjectsByType<Warehouse>(FindObjectsSortMode.None);

        if (warehouses.Length == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: На карте нет ни одного склада!");
            return null;
        }

        Debug.Log($"[Routing] {gameObject.name}: Найдено {warehouses.Length} складов. Проверяю доступность по дорогам...");

        // Проверяем доступность дорог
        if (_gridSystem == null || _roadManager == null || _identity == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Системы не инициализированы, выбираю склад с балансировкой по прямой");
            return FindBalancedWarehouseByDistance(warehouses);
        }

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Граф дорог пуст, выбираю склад с балансировкой по прямой");
            return FindBalancedWarehouseByDistance(warehouses);
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

        // ✅ БАЛАНСИРОВКА СКЛАДОВ:
        // Собираем информацию о каждом складе: расстояние + нагрузка
        var warehouseInfo = new System.Collections.Generic.List<(Warehouse warehouse, int distance, int producerCount)>();

        foreach (var wh in warehouses)
        {
            var whIdentity = wh.GetComponent<BuildingIdentity>();
            if (whIdentity == null)
                continue;

            var whAccessPoints = LogisticsPathfinder.FindAllRoadAccess(whIdentity.rootGridPosition, _gridSystem, roadGraph);

            // Находим минимальное расстояние до этого склада
            int minDistToWarehouse = int.MaxValue;
            foreach (var accessPoint in whAccessPoints)
            {
                if (distancesFromMe.TryGetValue(accessPoint, out int dist) && dist < minDistToWarehouse)
                {
                    minDistToWarehouse = dist;
                }
            }

            // Если склад недостижим по дорогам - пропускаем
            if (minDistToWarehouse == int.MaxValue)
            {
                Debug.LogWarning($"[Routing] {gameObject.name}: Склад {wh.name} на {whIdentity.rootGridPosition} недостижим по дорогам!");
                continue;
            }

            // Подсчитываем нагрузку (сколько производителей уже используют этот склад)
            int producerCount = CountProducersForWarehouse(wh);

            warehouseInfo.Add((wh, minDistToWarehouse, producerCount));

            Debug.Log($"[Routing] {gameObject.name}: Склад {wh.name} - дистанция: {minDistToWarehouse}, производителей: {producerCount}");
        }

        if (warehouseInfo.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Склады найдены, но нет дороги к ним!");
            return null;
        }

        // ✅ ВЫБОР С БАЛАНСИРОВКОЙ:
        // Сортируем: сначала по нагрузке (меньше = лучше), затем по расстоянию (ближе = лучше)
        warehouseInfo.Sort((a, b) =>
        {
            // Приоритет 1: Меньше нагрузка
            int loadComparison = a.producerCount.CompareTo(b.producerCount);
            if (loadComparison != 0)
                return loadComparison;

            // Приоритет 2: Меньше расстояние
            return a.distance.CompareTo(b.distance);
        });

        var bestWarehouse = warehouseInfo[0];

        Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН склад: {bestWarehouse.warehouse.name} (дистанция: {bestWarehouse.distance}, нагрузка: {bestWarehouse.producerCount} производителей)");

        return bestWarehouse.warehouse;
    }

    /// <summary>
    /// ✅ НОВОЕ: Подсчитывает, сколько производителей уже используют данный склад как outputDestination
    /// </summary>
    private int CountProducersForWarehouse(Warehouse warehouse)
    {
        int count = 0;

        // Находим все здания с маршрутизацией
        BuildingResourceRouting[] allRoutings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);

        foreach (var routing in allRoutings)
        {
            // Пропускаем себя
            if (routing == this)
                continue;

            // Проверяем, использует ли это здание наш склад как получатель Output
            if (ReferenceEquals(routing.outputDestination, warehouse))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// ✅ НОВОЕ: Выбирает склад с балансировкой по прямому расстоянию (fallback без дорог)
    /// </summary>
    private Warehouse FindBalancedWarehouseByDistance(Warehouse[] warehouses)
    {
        // Собираем информацию: склад + расстояние + нагрузка
        var warehouseInfo = new System.Collections.Generic.List<(Warehouse warehouse, float distance, int producerCount)>();

        foreach (var wh in warehouses)
        {
            float dist = Vector3.Distance(transform.position, wh.transform.position);
            int producerCount = CountProducersForWarehouse(wh);

            warehouseInfo.Add((wh, dist, producerCount));
        }

        // Сортируем: сначала по нагрузке, затем по расстоянию
        warehouseInfo.Sort((a, b) =>
        {
            int loadComparison = a.producerCount.CompareTo(b.producerCount);
            if (loadComparison != 0)
                return loadComparison;

            return a.distance.CompareTo(b.distance);
        });

        if (warehouseInfo.Count > 0)
        {
            var best = warehouseInfo[0];
            Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН склад (по прямой) {best.warehouse.name} - расстояние: {best.distance:F1}, нагрузка: {best.producerCount}");
            return best.warehouse;
        }

        return null;
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