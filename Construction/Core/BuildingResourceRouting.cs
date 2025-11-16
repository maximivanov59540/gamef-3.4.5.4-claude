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
    // Кэшированные интерфейсы
    public IResourceReceiver outputDestination { get; private set; }
    public IResourceProvider inputSource { get; private set; }
    private BuildingIdentity _identity;
    private float _retryTimer = 0f;
    
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
            // Используем указанное здание
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
            // Автопоиск ближайшего склада
            inputSource = FindNearestWarehouse();
            
            if (inputSource != null)
            {
                _inputSourceName = $"Склад (авто) на {inputSource.GetGridPosition()}";
                Debug.Log($"[Routing] {gameObject.name}: Input ← автопоиск склада на {inputSource.GetGridPosition()}");
            }
            else
            {
                _inputSourceName = "НЕ НАЙДЕН!";
                Debug.LogWarning($"[Routing] {gameObject.name}: Input источник НЕ НАЙДЕН! Постройте склад.");
            }
        }
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