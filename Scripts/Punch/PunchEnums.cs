
public enum PunchHand
{
    Left = 0,
    Right = 1,
    Any = 2,
}

public enum PunchTargetType
{
    Jab = 0,
    CrossLeft = 1,   // pad on the player's left side, meant for the right hand
    CrossRight = 2,  // pad on the player's right side, meant for the left hand
    HookLeft = 3,    // temple height, player's left side, right hand
    HookRight = 4,   // temple height, player's right side, left hand
    UpperCut = 5,    // low center pad
    GamePad = 6,     // the two existing BoxPad objects of the station game
}

public enum PunchQuality
{
    Clean = 0,
    Sloppy = 1,
    Invalid = 2,
}
