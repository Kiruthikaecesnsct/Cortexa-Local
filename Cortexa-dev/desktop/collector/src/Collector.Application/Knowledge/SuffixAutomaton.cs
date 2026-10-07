namespace Collector.Application.Knowledge;

internal sealed class SuffixAutomaton
{
    private readonly List<Dictionary<char, int>> _next = [new()];
    private readonly List<int> _link = [-1];
    private readonly List<int> _length = [0];
    private int _last;

    public SuffixAutomaton(string pattern)
    {
        foreach (var character in pattern)
        {
            Extend(character);
        }
    }

    public int LongestMatchIn(string text)
    {
        var state = 0;
        var matched = 0;
        var best = 0;
        foreach (var character in text)
        {
            while (state != 0 && !_next[state].ContainsKey(character))
            {
                state = _link[state];
                matched = _length[state];
            }

            if (_next[state].TryGetValue(character, out var following))
            {
                state = following;
                matched++;
                best = Math.Max(best, matched);
            }
        }

        return best;
    }

    private void Extend(char character)
    {
        var current = NewState(_length[_last] + 1, -1, null);
        var walker = _last;
        while (walker != -1 && !_next[walker].ContainsKey(character))
        {
            _next[walker][character] = current;
            walker = _link[walker];
        }

        _link[current] = walker == -1 ? 0 : Resolve(walker, character);
        _last = current;
    }

    private int Resolve(int walker, char character)
    {
        var target = _next[walker][character];
        if (_length[walker] + 1 == _length[target])
        {
            return target;
        }

        var clone = NewState(_length[walker] + 1, _link[target], _next[target]);
        RedirectToClone(walker, character, target, clone);
        _link[target] = clone;
        return clone;
    }

    private void RedirectToClone(int walker, char character, int target, int clone)
    {
        while (walker != -1 && _next[walker].TryGetValue(character, out var existing) && existing == target)
        {
            _next[walker][character] = clone;
            walker = _link[walker];
        }
    }

    private int NewState(int length, int link, Dictionary<char, int>? transitions)
    {
        _next.Add(transitions is null ? [] : new Dictionary<char, int>(transitions));
        _link.Add(link);
        _length.Add(length);
        return _next.Count - 1;
    }
}
