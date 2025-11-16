using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Упрощённая тележка, работающая в цикле для ОДНОГО производственного здания.
/// 
/// ЦИКЛ РАБОТЫ:
/// 1. Загрузить Output (продукцию) из дома
/// 2. Отвезти Output к получателю (склад/другое здание)
/// 3. Разгрузить Output
/// 4. Загрузить Input (сырьё) на месте разгрузки
/// 5. Вернуться домой с Input
/// 6. Разгрузить Input
/// 7. Повторить
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
public class CartAgent : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //                      СОСТОЯНИЯ (6 вместо 11!)
    // ════════════════════════════════════════════════════════════════
    
    private enum State
    {
        Idle,               // Ждёт, пока накопится продукция
        LoadingOutput,      // Грузит продукцию из дома (корутина)
        DeliveringOutput,   // Везёт продукцию к получателю
        UnloadingOutput,    // Разгружает продукцию (корутина)
        LoadingInput,       // Грузит сырьё (корутина)
        ReturningWithInput  // Везёт сырьё домой
        // UnloadingInput будет внутри ReturningWithInput при прибытии
    }
    
    private State _state = State.Idle;
    private Coroutine _activeCoroutine;
    
    // ════════════════════════════════════════════════════════════════
    //                          НАСТРОЙКИ
    // ════════════════════════════════════════════════════════════════
    
    [Header("Настройки Движения")]
    [Tooltip("Скорость движения (юнитов/сек)")]
    public float moveSpeed = 5f;
    
    [Tooltip("Время (сек) на погрузку и разгрузку")]
    public float loadingTime = 2.0f;
    
    [Tooltip("Грузоподъёмность (сколько может везти за раз)")]
    public float cargoCapacity = 1f;
    
    // ════════════════════════════════════════════════════════════════
    //                    ССЫЛКИ НА "ДОМ"
    // ════════════════════════════════════════════════════════════════
    
    private Transform _homeBase;
    private Vector2Int _homePosition;
    private BuildingOutputInventory _homeOutput;
    private BuildingInputInventory _homeInput;
    private BuildingResourceRouting _routing;
    
    // ════════════════════════════════════════════════════════════════
    //                      ТЕКУЩИЙ ГРУЗ
    // ════════════════════════════════════════════════════════════════
    
    private float _cargoAmount = 0f;
    private ResourceType _cargoType;
    
    // ════════════════════════════════════════════════════════════════
    //                    СИСТЕМЫ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════
    
    private GridSystem _gridSystem;
    private RoadManager _roadManager;
    
    // Навигация
    private List<Vector2Int> _currentPath;
    private int _pathIndex;
    private Vector3 _targetPosition;
    
    // ════════════════════════════════════════════════════════════════
    //                      ИНИЦИАЛИЗАЦИЯ
    // ════════════════════════════════════════════════════════════════
    
    void Start()
    {
        // 1. Находим "дом" (родительский объект)
        _homeBase = transform.parent;
        if (_homeBase == null)
        {
            Debug.LogError($"[CartAgent] {name} должен быть дочерним объектом здания!", this);
            enabled = false;
            return;
        }
        
        // 2. Находим компоненты на "доме"
        _homeOutput = _homeBase.GetComponent<BuildingOutputInventory>();
        _homeInput = _homeBase.GetComponent<BuildingInputInventory>();
        _routing = _homeBase.GetComponent<BuildingResourceRouting>();
        
        // Проверяем обязательные компоненты
        if (_homeOutput == null)
        {
            Debug.LogError($"[CartAgent] {name}: На базе {_homeBase.name} нет BuildingOutputInventory!", this);
            enabled = false;
            return;
        }
        
        if (_routing == null)
        {
            Debug.LogError($"[CartAgent] {name}: На базе {_homeBase.name} нет BuildingResourceRouting!", this);
            enabled = false;
            return;
        }
        
        // 3. Находим глобальные системы
        _gridSystem = FindFirstObjectByType<GridSystem>();
        _roadManager = RoadManager.Instance;
        
        if (_gridSystem == null)
        {
            Debug.LogError($"[CartAgent] {name}: Не найден GridSystem!", this);
            enabled = false;
            return;
        }
        
        if (_roadManager == null)
        {
            Debug.LogError($"[CartAgent] {name}: Не найден RoadManager!", this);
            enabled = false;
            return;
        }
        
        // 4. Запоминаем "адрес" дома
        var identity = _homeBase.GetComponent<BuildingIdentity>();
        if (identity != null)
        {
            _homePosition = identity.rootGridPosition;
        }
        else
        {
            _gridSystem.GetXZ(_homeBase.position, out int hx, out int hz);
            _homePosition = new Vector2Int(hx, hz);
        }
        
        // 5. Ставим тележку на позицию дома
        transform.position = _homeBase.position;
        
        Debug.Log($"[CartAgent] {name} инициализирован для {_homeBase.name}");
    }
    
    // ════════════════════════════════════════════════════════════════
    //                      ГЛАВНЫЙ ЦИКЛ
    // ════════════════════════════════════════════════════════════════
    
    void Update()
    {
        switch (_state)
        {
            case State.Idle:
                // Проверяем два варианта выхода из Idle:

                // 1. Если есть продукция для отправки
                if (_homeOutput != null && _homeOutput.HasAtLeastOneUnit())
                {
                    SetState(State.LoadingOutput);
                    break;
                }

                // 2. ✅ НОВОЕ: Если нужно сырье, но нет продукции - едем за сырьем напрямую
                if (ShouldFetchInputDirectly())
                {
                    Debug.Log($"[CartAgent] {name}: Нет продукции, но нужно сырье. Еду за Input напрямую!");
                    StartDirectInputFetch();
                }
                break;

            case State.DeliveringOutput:
            case State.ReturningWithInput:
                // В пути - просто едем
                FollowPath();
                break;

            // Остальные состояния управляются корутинами
            // (LoadingOutput, UnloadingOutput, LoadingInput)
        }
    }
    
    // ════════════════════════════════════════════════════════════════
    //                   УПРАВЛЕНИЕ СОСТОЯНИЯМИ
    // ════════════════════════════════════════════════════════════════
    
    private void SetState(State newState)
    {
        if (_state == newState) return;
        
        // Останавливаем активную корутину
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }
        
        _state = newState;
        
        // Запускаем корутину для нового состояния
        switch (_state)
        {
            case State.LoadingOutput:
                _activeCoroutine = StartCoroutine(LoadOutputCoroutine());
                break;
                
            case State.UnloadingOutput:
                _activeCoroutine = StartCoroutine(UnloadOutputCoroutine());
                break;
                
            case State.LoadingInput:
                _activeCoroutine = StartCoroutine(LoadInputCoroutine());
                break;
        }
    }
    
    // ════════════════════════════════════════════════════════════════
    //                         КОРУТИНЫ
    // ════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Шаг 1: Загружаем Output (продукцию) из дома
    /// </summary>
    private IEnumerator LoadOutputCoroutine()
    {
        Debug.Log($"[CartAgent] {name}: LoadOutputCoroutine начата");
        yield return new WaitForSeconds(loadingTime);

        if (_homeOutput == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: _homeOutput == null при загрузке!");
            SetState(State.Idle);
            yield break;
        }

        // Забираем продукцию из дома
        _cargoType = _homeOutput.GetProvidedResourceType();
        _cargoAmount = _homeOutput.TryTakeResource(_cargoType, cargoCapacity);

        if (_cargoAmount <= 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: Не удалось загрузить продукцию из {_homeBase.name}");
            SetState(State.Idle);
            yield break;
        }

        Debug.Log($"[CartAgent] {name} загрузил {_cargoAmount} {_cargoType} из {_homeBase.name}");

        // Едем к получателю
        IResourceReceiver destination = _routing.outputDestination;

        if (destination == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: Output destination не настроен!");
            ReturnOutputToHome();
            SetState(State.Idle);
            yield break;
        }

        Debug.Log($"[CartAgent] {name}: Ищу путь к получателю {destination.GetGridPosition()}...");
        if (FindPathTo(destination.GetGridPosition()))
        {
            Debug.Log($"[CartAgent] {name}: Путь найден, везу {_cargoAmount} {_cargoType} к {destination.GetGridPosition()}");
            SetState(State.DeliveringOutput);
        }
        else
        {
            Debug.LogWarning($"[CartAgent] {name}: Не найден путь к {destination.GetGridPosition()}");
            ReturnOutputToHome();
            SetState(State.Idle);
        }
    }
    
    /// <summary>
    /// Шаг 2: Разгружаем Output в пункт назначения
    /// </summary>
    private IEnumerator UnloadOutputCoroutine()
    {
        Debug.Log($"[CartAgent] {name}: UnloadOutputCoroutine начата, разгружаю {_cargoAmount} {_cargoType}");
        yield return new WaitForSeconds(loadingTime);

        IResourceReceiver destination = _routing.outputDestination;

        if (destination == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: Output destination исчез!");
            ReturnOutputToHome();
            SetState(State.Idle);
            yield break;
        }

        // Пытаемся разгрузить
        float delivered = destination.TryAddResource(_cargoType, _cargoAmount);
        _cargoAmount -= delivered;

        if (_cargoAmount > 0)
        {
            // НЕ СМОГЛИ РАЗГРУЗИТЬ ВСЁ - СКЛАД ПОЛОН!
            Debug.LogWarning($"[CartAgent] {name}: Склад полон! Осталось {_cargoAmount} {_cargoType}. Жду 2 сек...");

            // Ждём 2 секунды и повторяем попытку
            yield return new WaitForSeconds(2f);
            _activeCoroutine = StartCoroutine(UnloadOutputCoroutine());
            yield break;
        }

        Debug.Log($"[CartAgent] {name} разгрузил {delivered} {_cargoType} в {destination.GetGridPosition()}");

        _cargoAmount = 0;

        // ✅ КЛЮЧЕВОЙ МОМЕНТ: Сразу пытаемся загрузить Input!
        Debug.Log($"[CartAgent] {name}: Output разгружен, пытаюсь загрузить Input...");
        TryLoadInput();
    }
    
    /// <summary>
    /// Шаг 3: Пытаемся загрузить Input на текущей позиции
    /// </summary>
    private void TryLoadInput()
    {
        // ✅ ИСПРАВЛЕНИЕ: Проверяем, требует ли здание Input ВООБЩЕ
        // Для лесопилки _homeInput != null, но requiredResources пустой
        if (_homeInput == null || _homeInput.requiredResources == null || _homeInput.requiredResources.Count == 0)
        {
            Debug.Log($"[CartAgent] {name}: Дом не требует сырья, возвращаюсь пустым");
            ReturnHomeEmpty();
            return;
        }

        // Какой ресурс нужен?
        ResourceType neededType = GetNeededInputType();
        if (neededType == ResourceType.None)
        {
            Debug.Log($"[CartAgent] {name}: Все слоты Input заполнены (≥90%), возвращаюсь пустым");
            ReturnHomeEmpty();
            return;
        }

        Debug.Log($"[CartAgent] {name}: Нужен Input: {neededType}");

        // Есть ли источник?
        IResourceProvider source = _routing.inputSource;
        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: Input source не настроен!");
            ReturnHomeEmpty();
            return;
        }

        // ✅ НОВОЕ: Если источник - склад, но есть производитель, НЕ БРАТЬ со склада
        bool isWarehouse = source is Warehouse;
        if (isWarehouse && HasProducerForResource(neededType))
        {
            Debug.Log($"[CartAgent] {name}: НЕ беру {neededType} со склада! Найден производитель. Жду производства. Возвращаюсь домой пустым.");
            ReturnHomeEmpty();
            return;
        }

        // Есть ли ресурс в источнике?
        float availableAmount = source.GetAvailableAmount(neededType);
        Debug.Log($"[CartAgent] {name}: В источнике {source.GetGridPosition()} доступно {availableAmount} {neededType}");

        if (availableAmount < 1f)
        {
            Debug.LogWarning($"[CartAgent] {name}: В источнике нет {neededType}, возвращаюсь пустым");
            ReturnHomeEmpty();
            return;
        }

        // Всё ОК - грузим!
        Debug.Log($"[CartAgent] {name}: Начинаю загрузку {neededType} из {source.GetGridPosition()}");
        SetState(State.LoadingInput);
    }
    
    /// <summary>
    /// Шаг 4: Загружаем Input с текущей позиции
    /// </summary>
    private IEnumerator LoadInputCoroutine()
    {
        Debug.Log($"[CartAgent] {name}: LoadInputCoroutine начата, ждем {loadingTime} сек...");
        Debug.Log($"[CartAgent] {name}: Текущая позиция: {transform.position}");
        yield return new WaitForSeconds(loadingTime);

        ResourceType neededType = GetNeededInputType();
        if (neededType == ResourceType.None)
        {
            Debug.LogWarning($"[CartAgent] {name}: LoadInputCoroutine - neededType стал None! (возможно, слоты заполнились)");
            ReturnHomeEmpty();
            yield break;
        }

        IResourceProvider source = _routing.inputSource;
        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: LoadInputCoroutine - inputSource == null! (маршрутизация не настроена)");
            ReturnHomeEmpty();
            yield break;
        }

        Debug.Log($"[CartAgent] {name}: Источник Input: {source.GetGridPosition()}, тип: {source.GetType().Name}");

        // Проверяем доступное количество ПЕРЕД попыткой взять
        float availableAtSource = source.GetAvailableAmount(neededType);
        Debug.Log($"[CartAgent] {name}: В источнике доступно {availableAtSource} {neededType}");

        if (availableAtSource < 0.1f)
        {
            Debug.LogWarning($"[CartAgent] {name}: В источнике недостаточно {neededType}, возвращаюсь пустым");
            ReturnHomeEmpty();
            yield break;
        }

        // Сколько можем взять?
        float spaceInHome = _homeInput.GetAvailableSpace(neededType);
        float amountToTake = Mathf.Min(cargoCapacity, spaceInHome);

        Debug.Log($"[CartAgent] {name}: Пытаюсь взять {amountToTake} {neededType} (место в доме: {spaceInHome}, грузоподъемность: {cargoCapacity})");

        // Берём ресурс
        _cargoAmount = source.TryTakeResource(neededType, amountToTake);
        _cargoType = neededType;

        Debug.Log($"[CartAgent] {name}: TryTakeResource вернул {_cargoAmount} (запрашивали {amountToTake})");

        if (_cargoAmount > 0)
        {
            Debug.Log($"[CartAgent] {name} успешно загрузил {_cargoAmount} {_cargoType} из {source.GetGridPosition()}");

            // Едем домой
            Debug.Log($"[CartAgent] {name}: Ищу путь домой к {_homePosition}...");
            if (FindPathTo(_homePosition))
            {
                Debug.Log($"[CartAgent] {name}: Путь домой найден, начинаю движение с {_cargoAmount} {_cargoType}");
                SetState(State.ReturningWithInput);
            }
            else
            {
                Debug.LogError($"[CartAgent] {name}: НЕ МОГУ НАЙТИ ПУТЬ ДОМОЙ к {_homePosition}!");
                ReturnInputToSource(source);
                GoHomeAndIdle();
            }
        }
        else
        {
            Debug.LogWarning($"[CartAgent] {name}: ОШИБКА: Не удалось загрузить {neededType} - TryTakeResource вернул 0, хотя GetAvailableAmount показал {availableAtSource}");
            ReturnHomeEmpty();
        }
    }
    
    // ════════════════════════════════════════════════════════════════
    //                  ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Определяет, какой Input нужен дому (первый незаполненный слот)
    /// </summary>
    private ResourceType GetNeededInputType()
    {
        if (_homeInput == null || _homeInput.requiredResources == null)
            return ResourceType.None;

        // ✅ ИСПРАВЛЕНИЕ: Если слотов нет вообще (например, лесопилка), возвращаем None
        if (_homeInput.requiredResources.Count == 0)
            return ResourceType.None;

        foreach (var slot in _homeInput.requiredResources)
        {
            if (slot.maxAmount <= 0) continue;

            float fillRatio = slot.currentAmount / slot.maxAmount;
            if (fillRatio < 0.9f) // Заполнен меньше чем на 90%
            {
                return slot.resourceType;
            }
        }

        return ResourceType.None;
    }

    /// <summary>
    /// ✅ НОВОЕ: Проверяет, нужно ли ехать за Input напрямую (без отправки Output)
    /// </summary>
    private bool ShouldFetchInputDirectly()
    {
        // Не нужно, если нет компонента Input
        if (_homeInput == null || _homeInput.requiredResources == null || _homeInput.requiredResources.Count == 0)
            return false;

        // Не нужно, если маршрут Input не настроен
        if (_routing == null || _routing.inputSource == null)
            return false;

        // Проверяем, есть ли хотя бы один слот, который нуждается в пополнении
        foreach (var slot in _homeInput.requiredResources)
        {
            if (slot.maxAmount <= 0) continue;

            float fillRatio = slot.currentAmount / slot.maxAmount;

            // Если слот заполнен меньше чем на 25% - срочно нужен Input!
            if (fillRatio < 0.25f)
            {
                // ✅ НОВОЕ: Проверяем, является ли inputSource складом
                bool isWarehouse = _routing.inputSource is Warehouse;

                // ✅ ЕСЛИ ИСТОЧНИК - СКЛАД, проверяем наличие производителя
                if (isWarehouse)
                {
                    // Проверяем, есть ли производитель для этого ресурса
                    if (HasProducerForResource(slot.resourceType))
                    {
                        Debug.Log($"[CartAgent] {name}: НЕ еду на склад! Найден производитель {slot.resourceType}. Жду производства.");
                        return false; // НЕ ЕХАТЬ - ЖДАТЬ производителя!
                    }
                }

                // Проверяем, есть ли этот ресурс в источнике
                float availableAtSource = _routing.inputSource.GetAvailableAmount(slot.resourceType);
                if (availableAtSource >= 1f)
                {
                    Debug.Log($"[CartAgent] {name}: ShouldFetchInputDirectly = TRUE. Слот {slot.resourceType} заполнен на {fillRatio*100:F0}%, в источнике доступно {availableAtSource}");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// ✅ НОВОЕ: Проверяет, существует ли производитель для указанного ресурса
    /// </summary>
    private bool HasProducerForResource(ResourceType resourceType)
    {
        // Находим все здания с BuildingOutputInventory
        BuildingOutputInventory[] allOutputs = FindObjectsByType<BuildingOutputInventory>(FindObjectsSortMode.None);

        foreach (var output in allOutputs)
        {
            // Пропускаем себя
            if (output.gameObject == _homeBase.gameObject)
                continue;

            // Проверяем тип производимого ресурса
            if (output.outputResource.resourceType == resourceType)
            {
                Debug.Log($"[CartAgent] {name}: Найден производитель {resourceType}: {output.gameObject.name}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ✅ НОВОЕ: Начинает поездку за Input напрямую (без отправки Output)
    /// </summary>
    private void StartDirectInputFetch()
    {
        IResourceProvider source = _routing.inputSource;

        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: StartDirectInputFetch - inputSource == null!");
            return;
        }

        Vector2Int sourcePosition = source.GetGridPosition();

        Debug.Log($"[CartAgent] {name}: StartDirectInputFetch - ищу путь к источнику {sourcePosition}");

        if (FindPathTo(sourcePosition))
        {
            Debug.Log($"[CartAgent] {name}: Путь к источнику Input найден, начинаю движение");
            // Переходим в состояние "едем за Input"
            // Используем ReturningWithInput, но без груза (просто едем к источнику)
            _state = State.ReturningWithInput;
            _cargoAmount = 0;
        }
        else
        {
            Debug.LogWarning($"[CartAgent] {name}: Не найден путь к источнику Input {sourcePosition}");
        }
    }

    /// <summary>
    /// ✅ НОВОЕ: Загружает Input при прямом прибытии к источнику (без разгрузки Output)
    /// </summary>
    private void TryLoadInputDirectly()
    {
        ResourceType neededType = GetNeededInputType();
        if (neededType == ResourceType.None)
        {
            Debug.Log($"[CartAgent] {name}: TryLoadInputDirectly - не нужен Input (слоты заполнены), возвращаюсь домой пустым");
            ReturnHomeEmpty();
            return;
        }

        IResourceProvider source = _routing.inputSource;
        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: TryLoadInputDirectly - inputSource == null!");
            ReturnHomeEmpty();
            return;
        }

        // Проверяем доступное количество
        float availableAtSource = source.GetAvailableAmount(neededType);
        Debug.Log($"[CartAgent] {name}: TryLoadInputDirectly - в источнике доступно {availableAtSource} {neededType}");

        if (availableAtSource < 1f)
        {
            Debug.LogWarning($"[CartAgent] {name}: TryLoadInputDirectly - в источнике недостаточно {neededType}, возвращаюсь домой пустым");
            ReturnHomeEmpty();
            return;
        }

        // Запускаем корутину загрузки Input
        Debug.Log($"[CartAgent] {name}: TryLoadInputDirectly - начинаю загрузку {neededType}");
        SetState(State.LoadingInput);
    }
    
    /// <summary>
    /// Возвращает Output обратно в дом (если не смогли отвезти)
    /// </summary>
    private void ReturnOutputToHome()
    {
        if (_cargoAmount > 0 && _homeOutput != null)
        {
            // Возвращаем как float (интерфейсный метод не подходит, используем старый метод)
            int amountToReturn = Mathf.FloorToInt(_cargoAmount);
            bool success = _homeOutput.TryAddResource(amountToReturn);
            if (success)
            {
                Debug.Log($"[CartAgent] {name} вернул {amountToReturn} {_cargoType} обратно в дом");
            }
            else
            {
                Debug.LogWarning($"[CartAgent] {name}: Не удалось вернуть {amountToReturn} {_cargoType} в дом (переполнен!)");
            }
        }
        _cargoAmount = 0;
    }
    
    /// <summary>
    /// Возвращает Input обратно источнику (если не смогли довезти домой)
    /// </summary>
    private void ReturnInputToSource(IResourceProvider source)
    {
        if (_cargoAmount > 0 && source is IResourceReceiver receiver)
        {
            receiver.TryAddResource(_cargoType, _cargoAmount);
            Debug.Log($"[CartAgent] {name} вернул {_cargoAmount} {_cargoType} обратно в источник");
        }
        _cargoAmount = 0;
    }
    
    /// <summary>
    /// Возвращается домой пустой
    /// </summary>
    private void ReturnHomeEmpty()
    {
        Debug.Log($"[CartAgent] {name}: Возвращаюсь домой пустым к {_homePosition}");

        if (FindPathTo(_homePosition))
        {
            Debug.Log($"[CartAgent] {name}: Путь домой найден, начинаю движение");
            SetState(State.ReturningWithInput); // ✅ ИСПРАВЛЕНИЕ: используем SetState() вместо прямого _state =
        }
        else
        {
            // Аварийная телепортация
            Debug.LogWarning($"[CartAgent] {name}: Не могу найти путь домой! Телепортируюсь");
            GoHomeAndIdle();
        }
    }
    
    /// <summary>
    /// Телепортация домой + сброс в Idle
    /// </summary>
    private void GoHomeAndIdle()
    {
        transform.position = _homeBase.position;
        _currentPath = null;
        _pathIndex = 0;
        _cargoAmount = 0;
        SetState(State.Idle);
    }
    
    // ════════════════════════════════════════════════════════════════
    //                  ЛОГИКА ДОСТИЖЕНИЯ ЦЕЛИ
    // ════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Вызывается, когда достигли конца пути
    /// </summary>
    private void OnPathFinished()
    {
        if (_state == State.DeliveringOutput)
        {
            // Приехали к получателю Output
            IResourceReceiver destination = _routing.outputDestination;

            if (destination != null && destination.CanAcceptCart())
            {
                SetState(State.UnloadingOutput);
            }
            else
            {
                // Получатель не может принять - ждём
                Debug.Log($"[CartAgent] {name} ждёт у получателя Output");
                // Остаёмся в DeliveringOutput, попробуем в следующем кадре
            }
        }
        else if (_state == State.ReturningWithInput)
        {
            // ✅ НОВАЯ ЛОГИКА: Проверяем, где мы находимся
            _gridSystem.GetXZ(transform.position, out int currentX, out int currentZ);
            Vector2Int currentPos = new Vector2Int(currentX, currentZ);

            // Проверяем, находимся ли мы дома (с некоторой погрешностью)
            bool isAtHome = Vector2Int.Distance(currentPos, _homePosition) < 2f;

            if (isAtHome)
            {
                // Вернулись домой
                if (_cargoAmount > 0)
                {
                    // С грузом - разгружаем
                    Debug.Log($"[CartAgent] {name}: Приехал домой с {_cargoAmount} {_cargoType}, разгружаю");
                    StartCoroutine(UnloadInputAtHomeCoroutine());
                }
                else
                {
                    // Пустые - в Idle
                    Debug.Log($"[CartAgent] {name}: Приехал домой пустым, возвращаюсь в Idle");
                    SetState(State.Idle);
                }
            }
            else
            {
                // ✅ НОВОЕ: Приехали к источнику Input (не домой)
                if (_cargoAmount == 0)
                {
                    Debug.Log($"[CartAgent] {name}: Приехал к источнику Input, начинаю загрузку");
                    // Пытаемся загрузить Input прямо здесь
                    TryLoadInputDirectly();
                }
                else
                {
                    // Странная ситуация - груз есть, но мы не дома
                    Debug.LogWarning($"[CartAgent] {name}: Приехал не домой с грузом {_cargoAmount} {_cargoType}! Возвращаюсь домой");
                    ReturnHomeEmpty();
                }
            }
        }
    }
    
    /// <summary>
    /// Разгружаем Input в дом (последний шаг цикла)
    /// </summary>
    private IEnumerator UnloadInputAtHomeCoroutine()
    {
        yield return new WaitForSeconds(loadingTime);
        
        if (_homeInput != null)
        {
            float delivered = _homeInput.TryAddResource(_cargoType, _cargoAmount);
            Debug.Log($"[CartAgent] {name} разгрузил {delivered} {_cargoType} в дом");
            _cargoAmount -= delivered;
        }
        
        _cargoAmount = 0;
        
        // Цикл завершён - возвращаемся в Idle
        SetState(State.Idle);
    }
    
    // ════════════════════════════════════════════════════════════════
    //            НАВИГАЦИЯ (СТАРЫЙ КОД - НЕ МЕНЯЕМ!)
    // ════════════════════════════════════════════════════════════════
    
    private void FollowPath()
    {
        if (_currentPath == null)
        {
            // Путь потерялся
            Debug.LogWarning($"[CartAgent] {name}: Путь потерялся!");
            
            if (_cargoAmount > 0)
            {
                if (_state == State.DeliveringOutput)
                    ReturnOutputToHome();
                else if (_state == State.ReturningWithInput)
                    ReturnInputToSource(_routing.inputSource);
            }
            
            GoHomeAndIdle();
            return;
        }
        
        // === СТАРЫЙ КОД FollowPath() ===
        Vector2Int currentCell;
        if (_pathIndex > 0 && _pathIndex <= _currentPath.Count)
            currentCell = _currentPath[_pathIndex - 1];
        else
            currentCell = _currentPath[0];
        
        RoadTile currentTile = _gridSystem.GetRoadTileAt(currentCell.x, currentCell.y);
        float currentMultiplier = 1.0f;
        if (currentTile != null && currentTile.roadData != null)
            currentMultiplier = currentTile.roadData.speedMultiplier;
        
        Vector3 newPos = Vector3.MoveTowards(
            transform.position, 
            _targetPosition, 
            moveSpeed * currentMultiplier * Time.deltaTime
        );
        
        transform.position = newPos;
        
        Vector3 direction = (_targetPosition - transform.position).normalized;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction);
        
        if (Vector3.Distance(transform.position, _targetPosition) < 0.1f)
        {
            _pathIndex++;
            if (_pathIndex >= _currentPath.Count)
            {
                OnPathFinished();
            }
            else
            {
                SetNewTargetNode();
            }
        }
    }
    
    private bool FindPathTo(Vector2Int destinationCell)
    {
        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0) return false;
        
        Vector2Int startBuildingCell;
        if (Vector3.Distance(transform.position, _homeBase.position) < 1f)
        {
            startBuildingCell = GetCurrentHomeCell();
            if (startBuildingCell.x == -1) return false;
        }
        else
        {
            _gridSystem.GetXZ(transform.position, out int sx, out int sz);
            startBuildingCell = new Vector2Int(sx, sz);
        }
        
        List<Vector2Int> startAccessPoints = LogisticsPathfinder.FindAllRoadAccess(
            startBuildingCell, _gridSystem, roadGraph);
        if (startAccessPoints.Count == 0) return false;
        
        List<Vector2Int> endAccessPoints = LogisticsPathfinder.FindAllRoadAccess(
            destinationCell, _gridSystem, roadGraph);
        if (endAccessPoints.Count == 0) return false;
        
        var distances = LogisticsPathfinder.Distances_BFS_Multi(
            startAccessPoints, 1000, roadGraph);
        
        Vector2Int bestEndCell = new Vector2Int(-1, -1);
        int minDistance = int.MaxValue;
        
        foreach (var endCell in endAccessPoints)
        {
            if (distances.TryGetValue(endCell, out int dist) && dist < minDistance)
            {
                minDistance = dist;
                bestEndCell = endCell;
            }
        }
        
        if (bestEndCell.x == -1) return false;
        
        _currentPath = null;
        foreach(var startCell in startAccessPoints)
        {
            var path = LogisticsPathfinder.FindActualPath(startCell, bestEndCell, roadGraph);
            if (path != null)
            {
                _currentPath = path;
                break;
            }
        }
        
        if (_currentPath != null && _currentPath.Count > 0)
        {
            if (startBuildingCell != _currentPath[0])
                _currentPath.Insert(0, startBuildingCell);
            
            if (destinationCell != _currentPath[_currentPath.Count - 1])
                _currentPath.Add(destinationCell);
            
            _pathIndex = 0;
            SetNewTargetNode();
            return true;
        }
        
        return false;
    }
    
    private void SetNewTargetNode()
    {
        Vector2Int targetCell = _currentPath[_pathIndex];
        _targetPosition = _gridSystem.GetWorldPosition(targetCell.x, targetCell.y);
        
        float offset = _gridSystem.GetCellSize() / 2f;
        _targetPosition.x += offset;
        _targetPosition.z += offset;
        _targetPosition.y += 0.1f;
    }
    
    private Vector2Int GetCurrentHomeCell()
    {
        if (_homeBase == null) return new Vector2Int(-1, -1);
        
        var identity = _homeBase.GetComponent<BuildingIdentity>();
        if (identity != null)
            return identity.rootGridPosition;
        
        _gridSystem.GetXZ(_homeBase.position, out int hx, out int hz);
        return new Vector2Int(hx, hz);
    }
    
    // ════════════════════════════════════════════════════════════════
    //              ДЛЯ ВИЗУАЛИЗАЦИИ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════
    
    public bool IsBusy()
    {
        return _state != State.Idle;
    }
    
    public List<Vector3> GetRemainingPathWorld()
    {
        var pathPoints = new List<Vector3>();
        
        if (_currentPath == null || _currentPath.Count == 0) 
            return pathPoints;
        
        pathPoints.Add(transform.position);
        pathPoints.Add(_targetPosition);
        
        for (int i = _pathIndex + 1; i < _currentPath.Count; i++)
        {
            var cell = _currentPath[i];
            var pos = _gridSystem.GetWorldPosition(cell.x, cell.y);
            
            float offset = _gridSystem.GetCellSize() / 2f;
            pos.x += offset;
            pos.z += offset;
            pos.y += 0.1f;
            
            pathPoints.Add(pos);
        }
        
        return pathPoints;
    }
}