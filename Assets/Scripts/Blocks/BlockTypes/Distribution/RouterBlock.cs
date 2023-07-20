using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Frontiers.Content;

public class RouterBlock : ItemBlock {
    public new RouterBlockType Type { get => (RouterBlockType)base.Type; protected set => base.Type = value; }

    Queue<DelayedItem> queuedItems = new();

    protected override void Update() {
        base.Update();
        OutputItems();
    }

    public override bool CanReciveItem(Item item) {
        return queuedItems.Count < Type.itemCapacity;
    }
}