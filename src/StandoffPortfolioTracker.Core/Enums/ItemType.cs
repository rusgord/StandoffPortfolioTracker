using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandoffPortfolioTracker.Core.Enums
{
    public enum ItemType
    {
        Skin,       // Скин на оружие (в дальнейшем разбить над подтипы)
        Sticker,    // Наклейка
        Charm,      // Брелок
        Container,  // Кейс/Бокс/Пак (в дальнейшем разбить над подтипы)
        Glove,      // Перчатки
        Knife,      // Ножи (в дальнейшем разбить над подтипы)
        Graffiti    // Граффити
    }
}