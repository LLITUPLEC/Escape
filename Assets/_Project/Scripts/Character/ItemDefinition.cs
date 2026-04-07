using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Character
{
    [CreateAssetMenu(menuName = "Project/Character/Item Definition", fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string itemId = "item_001";
        [SerializeField] private string displayName = "Предмет";

        [Header("Visual")]
        [SerializeField] private Sprite icon;

        [Header("Equip")]
        [SerializeField] private bool equippable = true;
        [SerializeField] private EquipmentSlotId slot = EquipmentSlotId.Helmet;

        [Header("Stats (additive)")]
        [SerializeField] private List<StatModifier> modifiers = new();

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public bool Equippable => equippable;
        public EquipmentSlotId Slot => slot;
        public IReadOnlyList<StatModifier> Modifiers => modifiers;

        [Serializable]
        public sealed class StatModifier
        {
            public string statId = StatId.Hp;
            public float add = 0;
        }
    }
}

