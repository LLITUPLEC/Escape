using UnityEngine;

namespace Project.Character
{
    /// <summary>Fixed descriptions only. The server owns activation, eligibility and every combat/reward modifier.</summary>
    public static class ConsumablePresentation
    {
        public const string Bounty="scroll_pve_bounty";
        public static bool IsKnown(string id)=>id=="elixir_assault"||id=="elixir_stone"||id=="elixir_gambit"||id==Bounty;
        public static string Name(string id)=>id switch{
            "elixir_assault"=>"Эликсир натиска","elixir_stone"=>"Каменная настойка","elixir_gambit"=>"Эликсир азарта",Bounty=>"Свиток щедрой добычи",_=>"Расходник"};
        public static string Effects(string id)=>id switch{
            "elixir_assault"=>"Урон +20%\nБроня −15%","elixir_stone"=>"Броня +20%\nУрон −10%",
            "elixir_gambit"=>"Крит. шанс +10 п.п.\nЛечение −50%",Bounty=>"Золото PvE ×1,5\nРуда PvE ×1,5",_=>""};
        public static string Explanation(string id)=>id==Bounty
            ?"1 час с момента активации. Действует в новых боях PvE, завершённых до окончания таймера. С аурой сервера учитывается наибольший бонус. Опыт, материя и шанс добычи остаются прежними."
            :"На один следующий бой PvE или «Дуэль Pro». Обычная дуэль, арена и «Спуск» сохраняют подготовленный эликсир. До подходящего боя заменить его нельзя.";
        public static Sprite Icon(string id)=>IsKnown(id)?Resources.Load<Sprite>("Expedition/Daily/039/"+id):null;
        public static void MarkReceived(ConsumableStatus status)
        {if(status!=null&&!status.localTimeKnown){status.localTimeKnown=true;status.localReceivedAt=Time.realtimeSinceStartup;}}
        public static long ServerNow(ConsumableStatus status)
        {MarkReceived(status);return status==null?0:status.server_now+(long)(Time.realtimeSinceStartup-status.localReceivedAt);}
        public static string BlockedReason(string id,ConsumableStatus status)
        {
            if(status==null||status.schema!=1)return"Обновите состояние инвентаря";
            if(id==Bounty)return status.bounty_active&&status.bounty_ends_at>ServerNow(status)?"Свиток уже действует":null;
            return string.IsNullOrEmpty(status.armed_item_id)?null:"Уже подготовлен: "+Name(status.armed_item_id);
        }
        public static string Error(string code)=>code switch{
            "consumable_already_armed"=>"Другой эликсир уже подготовлен. Он сохранится до следующего боя PvE или «Дуэль Pro».",
            "bounty_already_active"=>"Свиток уже действует. Повторная активация не продлевает его время.",
            "inventory_changed"=>"Предмет переместился или уже использован. Обновите инвентарь.",
            "consumable_catalog_unavailable"=>"Предмет пока недоступен. Обновите состояние инвентаря.",
            "bad_use_sequence" or "sequence_changed" or "request_compacted" or "request_id_reused"=>"Состояние активации изменилось. Откройте инвентарь заново и проверьте предмет перед новой активацией.",
            "stale_session" or "session_epoch_mismatch"=>"Аккаунт или сессия изменились. Откройте инвентарь заново.",
            _=>"Не удалось подтвердить активацию. Повторите запрос: один предмет повторно не спишется."};
    }
}
