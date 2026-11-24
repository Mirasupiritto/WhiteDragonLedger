using System.Collections.Generic;
using System.Linq;

namespace WhiteDragon.Ledger
{
    internal static class LiquidParts
    {
        // A water order requires at least half of the liquid volume to be water.
        internal const double WaterFraction = 0.5;
        internal static List<KeyValuePair<Liquid,int>> Read(GameItem item)
        {
            var parts=new List<KeyValuePair<Liquid,int>>();
            foreach (var liquid in Liquid.Liquids)
            {
                if (string.IsNullOrEmpty(liquid?.currentPart)) continue;
                int amount=item.GetTagReadonly(liquid.currentPart).GetInt();
                if (amount>0) parts.Add(new KeyValuePair<Liquid,int>(liquid,amount));
            }
            return parts.OrderByDescending(p => p.Value).ToList();
        }
    }
}
