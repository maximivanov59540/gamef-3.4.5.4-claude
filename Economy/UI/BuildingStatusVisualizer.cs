using UnityEngine;

/// <summary>
/// Показывает/прячет иконки состояния ("Zzz", "!")
/// над зданием в зависимости от его "мозгов" (Producer/Input).
/// </summary>
public class BuildingStatusVisualizer : MonoBehaviour
{
    [Header("Иконка Проблемы")]
    [Tooltip("Универсальная иконка проблемы (показывается при любой проблеме)")]
    public GameObject problemIcon;

    // --- Ссылки на "мозги" ---
    private ResourceProducer _producer;
    private BuildingInputInventory _inputInv;
    private BuildingOutputInventory _outputInv;

    private void Awake()
    {
        _producer = GetComponent<ResourceProducer>();
        _inputInv = GetComponent<BuildingInputInventory>();
        _outputInv = GetComponent<BuildingOutputInventory>();

        // Если у здания нет ни того, ни другого - скрипт не нужен
        if (_producer == null && _inputInv == null)
        {
            Destroy(this); // (Или 'this.enabled = false;')
            return;
        }

        // Прячем иконку на старте
        if (problemIcon) problemIcon.SetActive(false);
    }

    private void Update()
    {
        bool hasProblem = false;

        // --- 1. ПРОВЕРКА "ЗАПРОСА СЫРЬЯ" (нет сырья) ---
        if (_inputInv != null && _inputInv.IsRequesting)
        {
            hasProblem = true;
        }

        // --- 2. ПРОВЕРКА "СКЛАД ПОЛОН" ---
        if (!hasProblem && _outputInv != null && !_outputInv.HasSpace(1))
        {
            hasProblem = true;
        }

        // --- 3. ПРОВЕРКА "НЕТ ДОСТУПА К СКЛАДУ" ---
        if (!hasProblem && _producer != null && !_producer.GetHasWarehouseAccess())
        {
            hasProblem = true;
        }

        // --- 4. ПРОВЕРКА "НЕТ РАБОЧИХ" ---
        if (!hasProblem && _producer != null)
        {
            float workforceCap = _producer.GetWorkforceCap();
            if (!Mathf.Approximately(workforceCap, 1.0f))
            {
                hasProblem = true;
            }
        }

        // Показываем/прячем универсальную иконку проблемы
        if (problemIcon)
        {
            problemIcon.SetActive(hasProblem);
        }
    }
}