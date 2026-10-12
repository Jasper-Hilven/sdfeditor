namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

//Design philosophy: fully commandable software with 30 keys (3 keys per finger, thumbs are given to pointer finger)
public static class FingerKeyLayouts
{
    //Columns left to right across the letter block, declared first since static fields initialize in order
    private static readonly (InputKey Up, InputKey Home, InputKey Down)[] Columns =
    [
        (InputKey.LPinkUp, InputKey.LPinkHome, InputKey.LPinkDown),
        (InputKey.LRingUp, InputKey.LRingHome, InputKey.LRingDown),
        (InputKey.LMiddleUp, InputKey.LMiddleHome, InputKey.LMiddleDown),
        (InputKey.LIndexUp, InputKey.LIndexHome, InputKey.LIndexDown),
        (InputKey.LIndex2Up, InputKey.LIndex2Home, InputKey.LIndex2Down),
        (InputKey.RIndex2Up, InputKey.RIndex2Home, InputKey.RIndex2Down),
        (InputKey.RIndexUp, InputKey.RIndexHome, InputKey.RIndexDown),
        (InputKey.RMiddleUp, InputKey.RMiddleHome, InputKey.RMiddleDown),
        (InputKey.RRingUp, InputKey.RRingHome, InputKey.RRingDown),
        (InputKey.RPinkUp, InputKey.RPinkHome, InputKey.RPinkDown),
    ];

    public static readonly Dictionary<string, InputKey> AzertyBeFingerKeyToIndex = Build(
        ["a", "z", "e", "r", "t", "y", "u", "i", "o", "p"],
        ["q", "s", "d", "f", "g", "h", "j", "k", "l", "m"],
        ["w", "x", "c", "v", "b", "n", ",", ";", ":", "="]);

    public static readonly Dictionary<string, InputKey> QwertyFingerKeyToIndex = Build(
        ["q", "w", "e", "r", "t", "y", "u", "i", "o", "p"],
        ["a", "s", "d", "f", "g", "h", "j", "k", "l", ";"],
        ["z", "x", "c", "v", "b", "n", "m", ",", ".", "/"]);

    private static Dictionary<string, InputKey> Build(string[] top, string[] home, string[] bottom)
    {
        var map = new Dictionary<string, InputKey>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Columns.Length; i++)
        {
            map.Add(top[i], Columns[i].Up);
            map.Add(home[i], Columns[i].Home);
            map.Add(bottom[i], Columns[i].Down);
        }
        return map;
    }
}
public enum InputKey {
    LPinkUp, LPinkHome, LPinkDown, 
    RPinkUp, RPinkHome, RPinkDown, 
    LRingUp, LRingHome, LRingDown, 
    RRingUp, RRingHome, RRingDown,
    LMiddleUp, LMiddleHome, LMiddleDown, 
    RMiddleUp, RMiddleHome, RMiddleDown,
    LIndexUp, LIndexHome, LIndexDown, 
    RIndexUp, RIndexHome, RIndexDown,
    LIndex2Up, LIndex2Home, LIndex2Down, 
    RIndex2Up, RIndex2Home, RIndex2Down,
    
    Enter
}