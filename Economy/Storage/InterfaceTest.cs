using UnityEngine;

public class InterfaceTest : MonoBehaviour
{
    void Start()
    {
        // Найдём склад
        Warehouse warehouse = FindFirstObjectByType<Warehouse>();
        
        if (warehouse != null)
        {
            // Проверяем, что Warehouse реализует интерфейсы
            IResourceProvider provider = warehouse as IResourceProvider;
            IResourceReceiver receiver = warehouse as IResourceReceiver;
            
            Debug.Log($"Warehouse реализует IResourceProvider: {provider != null}");
            Debug.Log($"Warehouse реализует IResourceReceiver: {receiver != null}");
            
            if (provider != null)
            {
                Debug.Log($"Warehouse позиция: {provider.GetGridPosition()}");
                Debug.Log($"Warehouse доступно Wood: {provider.GetAvailableAmount(ResourceType.Wood)}");
            }
        }
        
        // Найдём производство
        BuildingOutputInventory output = FindFirstObjectByType<BuildingOutputInventory>();
        if (output != null)
        {
            IResourceProvider outProvider = output as IResourceProvider;
            Debug.Log($"OutputInventory реализует IResourceProvider: {outProvider != null}");
        }
        
        BuildingInputInventory input = FindFirstObjectByType<BuildingInputInventory>();
        if (input != null)
        {
            IResourceReceiver inReceiver = input as IResourceReceiver;
            Debug.Log($"InputInventory реализует IResourceReceiver: {inReceiver != null}");
        }
    }
}