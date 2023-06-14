using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Frontiers.Content;
using Frontiers.Assets;
using Frontiers.Pooling;
using System.Linq;

public class ConveyorBlock : ItemBlock {
    public new ConveyorBlockType Type { get => (ConveyorBlockType)base.Type; protected set => base.Type = value; }

    public static GameObjectPool conveyorItemPool;

    public class ConveyorItem {
        public GameObject itemGameObject;
        public Item item;
        public Vector2 startPosition;
        public float time;

        public ConveyorItem(Item item, Vector2 startPosition) {
            this.item = item;

            itemGameObject = conveyorItemPool.Take();
            itemGameObject.GetComponent<SpriteRenderer>().sprite = this.item.sprite;

            this.startPosition = startPosition;
            time = 0;
            itemGameObject.transform.position = startPosition;
        }

        public void ChangeConveyor(Vector2 startPosition) {
            this.startPosition = startPosition;
            time = 0;
            itemGameObject.transform.position = startPosition;
        }

        public void Update(Vector2 endPosition) {
            itemGameObject.transform.position = Vector2.Lerp(startPosition, endPosition, time);
        }

        public void End() {
            conveyorItemPool.Return(itemGameObject);
        }

        public bool HasEndedLerp() => time >= 1;
    }

    public List<ConveyorItem> items = new List<ConveyorItem>();
    public float backSpace;
    public bool aligned;

    public ItemBlock next;
    public ConveyorBlock nextAsConveyor;

    protected Vector2 endPosition;
    protected float itemSpace;

    public override void Set<T>(Vector2 position, Quaternion rotation, T type, int id, byte teamCode) {
        base.Set(position, rotation, type, id, teamCode);
        endPosition = GetFacingEdgePosition() + GetPosition();
        UpdateAdjacentBlocks();
    }

    public override void SetInventory() {
        inventory = null;
        itemSpace = 1f / Type.itemCapacity;
        hasInventory = true;
    }

    protected override void Update() {
        base.Update();

        int len = items.Count;
        if (len == 0) return;

        backSpace = 1f;

        float nextMax = 1f;
        float moved = Time.deltaTime * Type.itemSpeed;

        for (int i = 0; i < len; i++) {
            float nextPos = (i == 0 ? 100f : items[i - 1].time) - itemSpace;
            float maxMove = Mathf.Clamp(nextPos - items[i].time, 0, moved);

            items[i].time += maxMove;
            items[i].Update(endPosition);

            if (items[i].time > nextMax) items[i].time = nextMax;

            if (items[i].time >= 1f && Pass(items[i])) {
                items.RemoveAt(i);
                len = Mathf.Min(i, len);
            } else if (items[i].time < backSpace) {
                backSpace = items[i].time;
            }
        }
    }

    public override void GetAdjacentBlocks() {
        next = GetFacingBlock() as ItemBlock;
        nextAsConveyor = next as ConveyorBlock;
        aligned = nextAsConveyor != null && nextAsConveyor.GetOrientation() == GetOrientation();
    }

    public override void UpdateAdjacentBlocks() {
        if (!next) return;
        next.GetAdjacentBlocks();
    }

    public override bool CanReciveItem(Item item) {
        bool timeSpacing = items.Count == 0 || items.Last().time >= itemSpace;
        return timeSpacing && items.Count < Type.itemCapacity;
    }

    public bool IsFacingAt(ItemBlock block) => block == next;

    public override void ReciveItem(Block source, Item item) {
        if (items.Count >= Type.itemCapacity) return;

        items.Add(new ConveyorItem(item, GetSharedEdgePosition(source) + GetPosition()));
    }

    public void ReciveItem(Block source, ConveyorItem conveyorItem) {
        if (items.Count >= Type.itemCapacity) return;

        items.Add(conveyorItem);
        conveyorItem.ChangeConveyor(GetSharedEdgePosition(source) + GetPosition());
    }

    public bool Pass(ConveyorItem convItem) {
        Item item = convItem.item;

        if (item != null && next != null && next.GetTeam() == GetTeam() && next.CanReciveItem(item)) {
            if (nextAsConveyor != null) nextAsConveyor.ReciveItem(this, convItem);
            else {
                convItem.End();
                next.ReciveItem(this, item); 
            }     

            return true;
        }

        return false;
    }

    public override bool IsFlammable() {
        foreach (ConveyorItem convItem in items) if (convItem.item.flammability > 0) return true;
        return false;
    }

    public override void OnDestroy() {
        base.OnDestroy();

        if (!gameObject.scene.isLoaded) return;
        foreach (ConveyorItem convItem in items) convItem.End();
    }
}