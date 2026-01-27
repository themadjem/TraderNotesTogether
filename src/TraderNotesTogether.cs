using Vintagestory.API.Common;

namespace TraderNotesTogether
{
    class TraderNotesTogetherSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.World.Logger.Notification("Trader Notes Together Mod Systems Started!");
        }
    }
}
