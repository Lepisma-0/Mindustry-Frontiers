using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Frontiers.Content;
using UnityEngine.Tilemaps;
using Frontiers.Assets;
using MapLayer = Frontiers.Content.Maps.Map.MapLayer;

public class DrillBlock : ItemBlock {
    public new DrillBlockType Type { get => (DrillBlockType)base.Type; protected set => base.Type = value; }

    public float drillTime = 5f;
    private float nextDrillTime = 0f;

    private float rotorVelocity = 0f;
    private float maxRotorVelocity = 0f;
    private readonly float rotorVelocityChange = 0.02f;

    public Item drillItem;
    public Transform rotorTransfrom;

    public override void Set<T>(Vector2 position, Quaternion rotation, T type, int id, byte teamCode) {
        base.Set(position, rotation, type, id, teamCode);

        drillItem = GetItemFromTiles(out float yieldPercent);
        drillTime = GetDrillTime(yieldPercent);

        nextDrillTime = Time.time + drillTime;

        outputItems = new Item[1] { drillItem };
        inventory.SetAllowedItems(outputItems);

        maxRotorVelocity = yieldPercent * 5f;
    }

    protected override void SetSprites() {
        base.SetSprites();

        rotorTransfrom = transform.Find("Empty");
        rotorTransfrom.gameObject.AddComponent<SpriteRenderer>();
        SetOptionalSprite(rotorTransfrom, AssetLoader.GetSprite(Type.name + "-rotator"), out SpriteRenderer spriteRenderer);

        spriteRenderer.sortingLayerName = "Blocks";
        spriteRenderer.sortingOrder = 3;
    }

    public override void SetInventory() {
        base.SetInventory();
    }

    public override bool CanReciveItem(Item item) {
        return false;
    }

    public bool CanDrill(Item item) => !inventory.Full(item);

    protected override void Update() {
        base.Update();

        if (drillTime == -1f || drillItem == null) return;

        OutputItems();
        bool canDrill = CanDrill(drillItem);

        rotorVelocity = Mathf.Clamp(rotorVelocity + (canDrill ? rotorVelocityChange : -rotorVelocityChange), 0, maxRotorVelocity);
        rotorTransfrom.eulerAngles += new Vector3(0, 0, rotorVelocity);

        if (nextDrillTime <= Time.time && canDrill) {
            inventory.Add(drillItem, 1);
            nextDrillTime = Time.time + drillTime;
        }
    }

    public Item GetItemFromTiles(out float yieldPercent) {
        Item priorityItem = null;
        int itemCount = 0;
        int totalTiles = Type.size * Type.size;

        for (int x = 0; x < Type.size; x++) {
            for (int y = 0; y < Type.size; y++) {
                Vector2 position = GetGridPosition() + new Vector2(x, y);
                TileType tile = MapManager.Map.GetMapTileTypeAt(MapLayer.Ore, position);
                Item item = null;

                if (tile == null || tile.itemDrop == null) {
                    TileType floorTile = MapManager.Map.GetMapTileTypeAt(MapLayer.Ground, position);

                    if (floorTile == null || floorTile.itemDrop == null) continue;
                    item = floorTile.itemDrop;
                } else if (tile.itemDrop != null) {
                    item = tile.itemDrop;
                }

                if (item == null) continue;

                if ((priorityItem == null || priorityItem.hardness < item.hardness) && item.hardness <= Type.drillHardness) {
                    priorityItem = item;
                    itemCount = 1;
                } else if (priorityItem == item) {
                    itemCount++;
                }
            }
        }

        yieldPercent = (float)itemCount / totalTiles;
        return priorityItem;
    }

    public float GetDrillTime(float yieldPercent) {
        if (drillItem == null) return -1f;
        return (drillTime + 0.833f * drillItem.hardness) / yieldPercent / Type.drillRate;
    }
}