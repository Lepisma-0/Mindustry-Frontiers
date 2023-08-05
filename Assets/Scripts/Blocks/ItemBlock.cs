using Frontiers.Content;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Frontiers.FluidSystem;
using System;

public abstract class ItemBlock : Block {
    public new ItemBlockType Type { get => (ItemBlockType)base.Type; protected set => base.Type = value; }

    protected ItemBlock[] adjacentBlocks;
    protected ItemBlock[] reciverBlocks;
    protected int[] reciverBlockOrientations;
    public int reciverBlockIndex;

    protected Item[] acceptedItems;
    protected Item[] outputItems;

    public FluidInventory fluidInventory;
    public SpriteRenderer fluidSpriteRenderer;

    public override void Set<T>(Vector2 position, Quaternion rotation, T type, int id, byte teamCode) {
        base.Set(position, rotation, type, id, teamCode);

        GetAdjacentBlocks();
        UpdateAdjacentBlocks();
    }

    protected override void SetSprites() {
        base.SetSprites();

        if (Type.fluidSprite == null) return;

        Transform fluidTransform = new GameObject("Fluid", typeof(SpriteRenderer)).transform;
        fluidTransform.parent = transform;
        fluidTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        SpriteRenderer spriteRenderer = fluidTransform.GetComponent<SpriteRenderer>();
        fluidSpriteRenderer = spriteRenderer;

        spriteRenderer.sprite = Type.fluidSprite;
        spriteRenderer.color = new Color(1f, 1f, 1f, 0f);
        spriteRenderer.sortingLayerName = "Blocks";
        spriteRenderer.sortingOrder = 2;
    }

    public override void SetInventory() {
        hasItemInventory = Type.hasItemInventory;
        hasFluidInventory = Type.hasFluidInventory;

        if (hasItemInventory) {
            inventory = new Inventory(Type.allowsSingleItem, Type.itemCapacity, Type.itemMass);
        }

        if (hasFluidInventory) {
            fluidInventory = new FluidInventory(this, Type.fluidInventoryData);
            fluidInventory.OnVolumeChanged += OnVolumeChanged;
        }

        base.SetInventory();
    }

    public virtual void OnVolumeChanged(object sender, EventArgs e) {
        if (fluidSpriteRenderer == null) return;
        fluidSpriteRenderer.color = new Color(1f, 1f, 1f, fluidInventory.usedVolume / fluidInventory.data.maxVolume);
    }

    public override bool CanReciveItem(Item item, int orientation = 0) { 
        return base.CanReciveItem(item) && IsAcceptedItem(item) && !inventory.Full(item); 
    }

    public virtual bool IsAcceptedItem(Item item) => acceptedItems == null || acceptedItems.Length == 0 || acceptedItems.Contains(item);

    protected override void Update() {
        base.Update();
        fluidInventory?.Update();
    }

    public virtual void OutputItems() {
        int reciverBlockCount = reciverBlocks.Length;
        if (reciverBlockCount == 0 || (outputItems != null && (outputItems.Length == 0 || inventory.Empty(outputItems)))) return;

        Item currentItem = outputItems == null ? inventory.First() : inventory.First(outputItems);
        if (currentItem == null || !inventory.Has(currentItem, 1)) return;

        int offset = reciverBlockIndex;

        for(int io = offset; io < reciverBlockCount + offset; io++) {
            int i = io % reciverBlockCount;

            Item item = inventory.Has(currentItem, 1) ? currentItem : (outputItems == null ? inventory.First() : inventory.First(outputItems));
            if (item == null) break;

            ItemBlock itemBlock = reciverBlocks[i];
            int orientation = reciverBlockOrientations[i];

            if (itemBlock.CanReciveItem(item, orientation)) {
                itemBlock.ReciveItems(item, 1, orientation);
                inventory.Substract(item, 1);
            }

            reciverBlockIndex = (i + 1) % reciverBlockCount;
        }
    }

    public virtual void GetAdjacentBlocks() {
        (List<ItemBlock> adjacentBlockList, List<int> adjacentBlockOrientations) = MapManager.Map.GetAdjacentBlocks(this);

        List<ItemBlock> recivers = new();
        List<int> reciverOrientations = new();

        adjacentBlocks = new ItemBlock[adjacentBlockList.Count];

        for (int i = 0; i < adjacentBlockList.Count; i++) {
            ItemBlock itemBlock = adjacentBlockList[i];

            // If the block is facing to this one, don't give any items back
            if (itemBlock.Type.hasOrientation && itemBlock.GetFacingBlock() != this) {
                recivers.Add(itemBlock);
                reciverOrientations.Add(adjacentBlockOrientations[i]);
            }

            adjacentBlocks[i] = itemBlock;
        }

        reciverBlocks = recivers.ToArray();
        reciverBlockOrientations = reciverOrientations.ToArray();

        if (hasFluidInventory) SetFluidLinkedComponents(adjacentBlocks);
    }

    public void SetFluidLinkedComponents(ItemBlock[] adjacentBlocks) {
        List<FluidInventory> fluidComponents = new();
        foreach (ItemBlock itemBlock in adjacentBlocks) if (itemBlock.hasFluidInventory) fluidComponents.Add(itemBlock.fluidInventory);
        fluidInventory.SetLinkedComponents(fluidComponents.ToArray());
    }

    public virtual void UpdateAdjacentBlocks() {
        int count = this.adjacentBlocks.Length;
        List<ItemBlock> adjacentBlocks = new();

        for (int i = 0; i < count; i++) {
            ItemBlock adjacentBlock = this.adjacentBlocks[i];

            if (adjacentBlock == null) continue;
            adjacentBlock.GetAdjacentBlocks();

            adjacentBlocks.Add(adjacentBlock);
        }

        this.adjacentBlocks = adjacentBlocks.ToArray();
    }

    public virtual bool IsFlammable() {
        if (inventory == null) return false;
        foreach (KeyValuePair<Item, int> pair in inventory.items) if (pair.Key.flammability > 0f && pair.Value > 0) return true;
        return false;
    }

    public virtual bool IsExplosive() {
        if (inventory == null) return false;
        foreach (KeyValuePair<Item, int> pair in inventory.items) if (pair.Key.explosiveness > 0f && pair.Value > 0) return true;
        return false;
    }

    public override void OnDestroy() {
        base.OnDestroy();
        if (!gameObject.scene.isLoaded) return;

        UpdateAdjacentBlocks();
    }
}
