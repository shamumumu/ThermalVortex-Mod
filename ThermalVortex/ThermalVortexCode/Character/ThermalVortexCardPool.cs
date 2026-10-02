using BaseLib.Abstracts;
using ThermalVortex.ThermalVortexCode.Extensions;
using Godot;

namespace ThermalVortex.ThermalVortexCode.Character;

public class ThermalVortexCardPool : CustomCardPoolModel
{
    public override string Title => ThermalVortex.CharacterId; //This is not a display name.

    public override string BigEnergyIconPath => "charui/big_energy.png".ImagePath();
    public override string TextEnergyIconPath => "charui/text_energy.png".ImagePath();


    /* These HSV values will determine the color of your card back.
    They are applied as a shader onto an already colored image,
    so it may take some experimentation to find a color you like.
    Generally they should be values between 0 and 1. */
    public override float H => 0.756f; //Hue; changes the color.
    public override float S => 0.77f; //Saturation
    public override float V => 0.75f; //Brightness

    //Alternatively, leave these values at 1 and provide a custom frame image.
    /*public override Texture2D CustomFrame(CustomCardModel card)
    {
        //This will attempt to load ThermalVortex/images/cards/frame.png
        return PreloadManager.Cache.GetTexture2D("cards/frame.png".ImagePath());
    }*/

    //Color of small card icons
    public override Color DeckEntryCardColor => ThermalVortex.Color;

    public override bool IsColorless => false;
}
