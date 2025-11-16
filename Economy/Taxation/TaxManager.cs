using System.Collections;
using UnityEngine;

/// <summary>
/// "Мозг" экономики, отвечающий за сбор налогов (денег).
/// </summary>
public class TaxManager : MonoBehaviour
{
    public static TaxManager Instance { get; private set; }

    // Ссылка на нашу КАЗНУ
    private MoneyManager _moneyManager;

    // Плавное начисление налогов (доход в секунду)
    private float _incomePerSecond;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    private void Start()
    {
        // На старте находим нашу казну.
        _moneyManager = MoneyManager.Instance;
        if (_moneyManager == null)
        {
            Debug.LogError("TaxManager не смог найти MoneyManager.Instance!");
        }

        // Запускаем корутину для обновления налогов каждую минуту
        StartCoroutine(MinuteTick());
    }

    private void Update()
    {
        // Плавное начисление денег каждый кадр
        if (_moneyManager != null && _incomePerSecond > 0)
        {
            _moneyManager.AddMoney(_incomePerSecond * Time.deltaTime);
        }
    }

    /// <summary>
    /// Корутина, которая срабатывает раз в 60 секунд.
    /// Пересчитывает общий доход от всех домов.
    /// </summary>
    private IEnumerator MinuteTick()
    {
        while (true)
        {
            // Ждем 1 минуту
            yield return new WaitForSeconds(60f);

            float totalIncomePerMinute = 0;

            // Находим все дома на сцене
            var allResidences = FindObjectsByType<Residence>(FindObjectsSortMode.None);

            foreach (var residence in allResidences)
            {
                // Собираем налог с каждого дома
                totalIncomePerMinute += residence.GetCurrentTax();
            }

            // Вычисляем доход в секунду
            _incomePerSecond = totalIncomePerMinute / 60f;

            Debug.Log($"[TaxManager] Общий доход в минуту: {totalIncomePerMinute}, доход в секунду: {_incomePerSecond}");
        }
    }

    /// <summary>
    /// Главный метод, который вызывают дома для уплаты налога (деньгами).
    /// (DEPRECATED - больше не используется, т.к. налоги собираются плавно)
    /// </summary>
    /// <param name="amount">Сколько денег платим</param>
    public void CollectTax(float amount)
    {
        if (amount <= 0 || _moneyManager == null) return;

        // Просто передаем деньги в казну
        _moneyManager.AddMoney(amount);
    }
}