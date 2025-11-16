using UnityEngine;

/// <summary>
/// Интерфейс для узла логистической сети, который может ОТДАТЬ ресурсы.
/// Реализуется: Warehouse, BuildingOutputInventory
/// </summary>
public interface IResourceProvider
{
    /// <summary>
    /// Позиция узла в сетке (для построения пути)
    /// </summary>
    Vector2Int GetGridPosition();
    
    /// <summary>
    /// Тип ресурса, который этот узел может отдать
    /// </summary>
    ResourceType GetProvidedResourceType();
    
    /// <summary>
    /// Сколько ресурса доступно для взятия
    /// </summary>
    /// <param name="type">Тип ресурса</param>
    /// <returns>Доступное количество</returns>
    float GetAvailableAmount(ResourceType type);
    
    /// <summary>
    /// Попытка забрать ресурс
    /// </summary>
    /// <param name="type">Тип ресурса</param>
    /// <param name="amount">Желаемое количество</param>
    /// <returns>Сколько реально удалось забрать</returns>
    float TryTakeResource(ResourceType type, float amount);
    
    /// <summary>
    /// Может ли узел принять тележку прямо сейчас?
    /// (Используется для очередей на складе)
    /// </summary>
    bool CanAcceptCart();
}