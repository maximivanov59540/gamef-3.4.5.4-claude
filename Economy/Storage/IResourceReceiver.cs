using UnityEngine;

/// <summary>
/// Интерфейс для узла логистической сети, который может ПРИНЯТЬ ресурсы.
/// Реализуется: Warehouse, BuildingInputInventory
/// </summary>
public interface IResourceReceiver
{
    /// <summary>
    /// Позиция узла в сетке (для построения пути)
    /// </summary>
    Vector2Int GetGridPosition();
    
    /// <summary>
    /// Принимает ли этот узел данный тип ресурса?
    /// </summary>
    /// <param name="type">Тип ресурса</param>
    bool AcceptsResource(ResourceType type);
    
    /// <summary>
    /// Сколько места доступно для данного ресурса
    /// </summary>
    /// <param name="type">Тип ресурса</param>
    /// <returns>Доступное место</returns>
    float GetAvailableSpace(ResourceType type);
    
    /// <summary>
    /// Попытка добавить ресурс
    /// </summary>
    /// <param name="type">Тип ресурса</param>
    /// <param name="amount">Желаемое количество</param>
    /// <returns>Сколько реально удалось добавить</returns>
    float TryAddResource(ResourceType type, float amount);
    
    /// <summary>
    /// Может ли узел принять тележку прямо сейчас?
    /// (Используется для очередей на складе)
    /// </summary>
    bool CanAcceptCart();
}