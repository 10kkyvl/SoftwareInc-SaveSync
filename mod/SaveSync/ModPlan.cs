using System.Collections.Generic;

namespace SaveSync
{
    public class CategoryAddition
    {
        public SoftwareType Owner;
        public SoftwareCategory Category;
    }

    public class FeatureAddition
    {
        public SoftwareType Owner;
        public FeatureBase Feature;
    }

    public class CategoryChange
    {
        public SoftwareCategory Current;
        public SoftwareCategory Target;
    }

    public class FeatureChange
    {
        public FeatureBase Current;
        public FeatureBase Target;
    }

    public class TypeChange
    {
        public SoftwareType Current;
        public SoftwareType Target;
    }

    public class AddOnAddition
    {
        public SoftwareType Owner;
        public SoftwareAddOn AddOn;
    }

    public class DependencyChange
    {
        public SpecFeature Current;
        public SpecFeature Target;
        public string Line;
    }

    public class TypeConflict
    {
        public string Type;
        public List<string> Mods;
        public List<List<string>> Lines;

        public TypeConflict(string type)
        {
            Type = type;
            Mods = new List<string>();
            Lines = new List<List<string>>();
        }
    }

    public class ChanceAddition
    {
        public CompanyType Owner;
        public KeyValuePair<string, string> Key;
        public float Chance;
    }

    public class ModPlan
    {
        public string Title;
        public string Refused;
        public bool NewToSave;
        public List<SoftwareType> NewTypes;
        public List<CategoryAddition> NewCategories;
        public List<FeatureAddition> NewFeatures;
        public List<CategoryChange> CategoryChanges;
        public List<FeatureChange> FeatureChanges;
        public List<TypeChange> TypeChanges;
        public List<DependencyChange> DependencyChanges;
        public List<AddOnAddition> NewAddOns;
        public List<CompanyType> NewCompanyTypes;
        public List<ChanceAddition> NewChances;
        public List<ChanceAddition> NewAddOnChances;
        public List<CompanyType> CompanyTargets;
        public Dictionary<string, List<string>> RefusedFeatures;
        public PersonalityGraph Personalities;
        public Dictionary<string, RandomNameGenerator> NewGenerators;
        public Dictionary<string, List<string>> Conflicts;
        public List<string> Touched;
        public List<string> Added;
        public List<string> Changed;
        public List<string> NotApplied;
        public List<string> Warnings;

        public ModPlan(string title)
        {
            Title = title;
            NewTypes = new List<SoftwareType>();
            NewCategories = new List<CategoryAddition>();
            NewFeatures = new List<FeatureAddition>();
            CategoryChanges = new List<CategoryChange>();
            FeatureChanges = new List<FeatureChange>();
            TypeChanges = new List<TypeChange>();
            DependencyChanges = new List<DependencyChange>();
            NewAddOns = new List<AddOnAddition>();
            NewCompanyTypes = new List<CompanyType>();
            NewChances = new List<ChanceAddition>();
            NewAddOnChances = new List<ChanceAddition>();
            CompanyTargets = new List<CompanyType>();
            RefusedFeatures = new Dictionary<string, List<string>>();
            NewGenerators = new Dictionary<string, RandomNameGenerator>();
            Conflicts = new Dictionary<string, List<string>>();
            Touched = new List<string>();
            Added = new List<string>();
            Changed = new List<string>();
            NotApplied = new List<string>();
            Warnings = new List<string>();
        }

        public bool HasChanges()
        {
            return Refused == null && (Added.Count > 0 || Changed.Count > 0);
        }

        public string Fingerprint()
        {
            if (Refused != null)
            {
                return Title + "|refused|" + Refused;
            }
            List<string> conflicts = new List<string>();
            foreach (KeyValuePair<string, List<string>> item in Conflicts)
            {
                conflicts.Add(item.Key + ":" + string.Join(",", item.Value.ToArray()));
            }
            return Title + "|" + string.Join(",", Added.ToArray()) + "|" + string.Join(",", Changed.ToArray()) + "|" + string.Join(";", conflicts.ToArray());
        }
    }
}
