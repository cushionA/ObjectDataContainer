# Zabuton Container Pack

[![Unity 6+](https://img.shields.io/badge/Unity-6000.0%2B-blue)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](Assets/com.zabuton.container-pack/LICENSE.md)

High-performance data containers for Unity with Source Generator support.

**[日本語版はこちら / Japanese](README_JP.md)**

## Features

- **Source Generator**: Auto-generates optimized containers from `[ContainerSetting]` attribute — zero-allocation Add/Remove/Get with SoA memory layout
- **Runtime Containers**: 21 ready-to-use utility containers for common game patterns

| Category | Container | Use Case |
|----------|-----------|----------|
| Spatial | `SpatialHashContainer2D / 3D` | Spatial hashing for neighbor queries |
| Pool | `PriorityPoolContainer<T>` | Priority-based pool with FIFO eviction |
| Buffer | `RingBufferContainer<T>` | Fixed-size circular buffer |
| Timer | `CooldownContainer` | Cooldown management per GameObject |
| Timer | `TimedDataContainer<T>` | Time-limited data storage |
| Timer | `NotifyTimedDataContainer<T>` | Timed data with expiry callbacks |
| State | `StateMapContainer<TState>` | State machine per GameObject |
| Group | `GroupContainer<TGroup>` | Group-based object management |
| Cache | `ComponentCache<T>` | Cached component lookups |
| Set | `SparseSetContainer<T>` | Sparse set (dense array + hash) |
| Accumulator | `ThresholdAccumulatorContainer` | Accumulator with threshold trigger |
| Probability | `WeightedSamplerContainer<T>` | Walker's Alias O(1) weighted sampling |
| Bit | `BitFlagTableContainer` | Hash × bit-flag table |
| Slot | `FixedSlotContainer<T>` | Fixed N-slot per entity |
| Relation | `FactionRelationContainer` | Faction relation matrix |
| Dedup | `HitDeduplicationContainer` | Hit deduplication (penetration-safe) |
| Combo | `FlagComboLookupContainer<TEffect>` | Flag combination → effect lookup |
| Score | `ScoredCandidateBuffer<T>` | Scored candidate buffer |
| Sequence | `MultiPartySequenceContainer<TState>` | Multi-party sequence |
| Effect | `StackableEffectContainer<TEffect>` | Stackable timed effects |

## Documentation

- [Wiki](https://github.com/cushionA/ObjectDataContainer/wiki)
- [Qiita article](https://qiita.com/cushionA/items/c86d2eceb3c11c56ca7f)

## Installation

### Via Git URL (Unity Package Manager)

```
https://github.com/cushionA/ObjectDataContainer.git#package
```

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Paste the URL above

### Requirements

- Unity 6000.0+
- Burst 1.8.23+
- Collections 2.4.3+
- Mathematics 1.3.2+

## Quick Start

### Source Generator

```csharp
using ODC.Attributes;

public struct Health { public float hp; public int maxHp; }
public struct Movement { public float speed; }
public class AI : MonoBehaviour { public float hpRate; }

[ContainerSetting(
    structType: new[] { typeof(Health), typeof(Movement) },
    classType: new[] { typeof(AI) }
)]
public partial class EnemyContainer
{
    public partial void Dispose();
}
```

```csharp
var container = new EnemyContainer(1000);

container.Add(gameObject,
    new Health { hp = 100, maxHp = 100 },
    new Movement { speed = 5f },
    ai
);

if (container.TryGetValue(gameObject, out var health, out var movement, out var ai, out int index))
{
    // Zero-allocation access
}

container.Remove(gameObject);
container.Dispose();
```

### Runtime Containers

```csharp
using ODC.Runtime;

var pool = new PriorityPoolContainer<BuffData>(maxCapacity: 10);
pool.Add(gameObject, buffData, priority: 5f, duration: 10f);
pool.TryAddOrEvict(newObj, newBuff, out var evicted, priority: 3f);
pool.Update(Time.deltaTime, onExpired: buff => Debug.Log($"Expired: {buff}"));
```

## License

[MIT](Assets/com.zabuton.container-pack/LICENSE.md)
