using UnityEngine;

[RequireComponent(typeof(BuildingIdentity))]
public class CentralWarehouse : MonoBehaviour
{
    [Tooltip("На сколько этот центральный склад увеличивает глобальный лимит хранения")]
    public float limitIncrease = 100f;

    void Start()
    {
        // При постройке - увеличиваем глобальный лимит хранения
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.IncreaseGlobalLimit(limitIncrease);
            Debug.Log($"[CentralWarehouse] Увеличен глобальный лимит на {limitIncrease}");
        }
    }

    void OnDestroy()
    {
        // При сносе - уменьшаем лимит
        // (Проверяем Instance на случай, если выходим из игры)
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.IncreaseGlobalLimit(-limitIncrease);
            Debug.Log($"[CentralWarehouse] Уменьшен глобальный лимит на {limitIncrease}");
        }
    }
}