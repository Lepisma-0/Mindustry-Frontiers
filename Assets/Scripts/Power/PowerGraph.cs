using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class PowerGraph {
    public List<PowerModule> powerConsumers = new();
    public List<PowerModule> powerGenerators = new();
    public List<PowerModule> powerStorages = new();
    public List<PowerModule> all = new();

    public PowerGraph() { 
    
    }

    public PowerGraph(PowerModule powerable) {
        Handle(powerable);
    }

    public PowerModule GetClosestTo(Vector2 position, out float closestDistance) {
        closestDistance = float.MaxValue;
        PowerModule closest = null;

        foreach(PowerModule powerable in all) {
            float distance = Vector2.Distance(powerable.GetPosition(), position);

            if (distance < closestDistance) {
                closestDistance = distance;
                closest = powerable;
            }
        }

        return closest;
    }

    public void Handle(PowerModule powerable) {
        if (powerable.ConsumesPower()) powerConsumers.Add(powerable);
        if (powerable.GeneratesPower()) powerGenerators.Add(powerable);
        if (powerable.StoresPower()) powerStorages.Add(powerable);
        all.Add(powerable);
        powerable.SetGraph(this);
    }

    public void Handle(PowerGraph other) {
        if (other.all.Count > all.Count) {
            other.Handle(this);
            return;
        }

        powerConsumers.AddRange(other.powerConsumers);
        powerGenerators.AddRange(other.powerGenerators);
        powerStorages.AddRange(other.powerStorages);
        all.AddRange(other.all);

        foreach(PowerModule powerable in other.all) powerable.SetGraph(this);

        PowerGraphManager.graphs.Remove(other);
    }

    public bool Contains(PowerModule powerable) {
        return all.Contains(powerable);
    }

    public void Update() {
        // Get the base amount of power
        float generated = PowerGenerated();
        float needed = PowerNeeded();

        if (generated > 0) {
            // If there is excess power, store it
            generated -= ChargeStorages(generated - needed);
        } else {
            // If there is lack of power, get from storages
            generated += DischargeStorages(needed - generated);
        }

        // Haha conditional tower
        float coverage = generated == 0 && needed == 0 ? 0 : needed == 0 ? 1f : Mathf.Min(1, generated / needed);

        foreach (PowerModule powerable in powerConsumers) {
            powerable.SetPowerPercent(coverage);
        }
    }

    public float DischargeStorages(float amount) {
        float stored = GetPowerStored();
        if (stored == 0f || amount == 0f) return 0f;

        float dischargePercentage = Mathf.Min(1f, amount / stored);

        foreach (PowerModule powerable in powerStorages) {
            powerable.DischargePower(dischargePercentage);
        }

        return Mathf.Min(stored, amount);
    }

    public float ChargeStorages(float amount) {
        float capacity = GetPowerCapacity();
        if (capacity == 0f || amount == 0f) return 0f;

        float chargePercentage = Mathf.Min(1f, amount / capacity);

        foreach (PowerModule powerable in powerStorages) {
            powerable.ChargePower(chargePercentage);
        }

        return Mathf.Min(capacity, amount);
    }

    public float PowerNeeded() {
        float usage = 0;

        foreach (PowerModule powerable in powerConsumers) {
            usage += powerable.GetPowerConsumption();
        }

        return usage;
    }

    public float PowerGenerated() {
        float usage = 0;

        foreach (PowerModule powerable in powerGenerators) {
            usage += powerable.GetPowerGeneration();
        }

        return usage;
    }

    public float GetPowerCapacity() {
        float storage = 0;

        foreach (PowerModule powerable in powerStorages) {
            storage += powerable.GetPowerCapacity();
        }

        return storage;
    }

    public float GetPowerStored() {
        float storage = 0;

        foreach (PowerModule powerable in powerStorages) {
            storage += powerable.GetStoredPower();
        }

        return storage;
    }

    public float GetPowerStorage() {
        float storage = 0;

        foreach(PowerModule powerable in powerStorages) {
            storage += powerable.GetMaxStorage();
        }

        return storage;
    }
}