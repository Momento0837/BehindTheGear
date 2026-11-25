using UnityEngine;

public sealed class DialoguePortraitLibrary : ScriptableObject
{
    [System.Serializable]
    public sealed class Expression
    {
        public string 표정;
        public Sprite image;
    }
    public Sprite ethan;
    public Sprite selena;
    public Expression[] ethanExpressions;
    public Expression[] selenaExpressions;

    public Sprite Resolve(string speaker, string expression)
    {
        string name = (speaker ?? string.Empty).Trim();
        bool isEthan = string.Equals(name, "Ethan", System.StringComparison.OrdinalIgnoreCase) || name == "이든";
        bool isSelena = string.Equals(name, "Selena", System.StringComparison.OrdinalIgnoreCase) || name == "셀레나";
        if (!isEthan && !isSelena) return null;
        var entries = isEthan ? ethanExpressions : selenaExpressions;
        string key = string.IsNullOrWhiteSpace(expression) ? "기본 표정" : expression.Trim();
        if (entries != null)
            foreach (var entry in entries)
                if (entry != null && entry.표정 == key)
                {
                    if (entry.image != null) return entry.image;
                    // Add the named Sprite later; Resources keeps the connection in builds as well.
                    var named = Resources.Load<Sprite>("Dialogue/Expressions/" + (isEthan ? "이든_" : "셀레나_") + key);
                    if (named != null) return named;
                    break;
                }
        // Old JSON and not-yet-assigned expression art retain the existing portrait.
        return isEthan ? ethan : selena;
    }
}
