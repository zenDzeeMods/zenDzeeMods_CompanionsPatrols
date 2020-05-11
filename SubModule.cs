﻿using Helpers;
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace zenDzeeMods_CompanionsPatrols
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            if (game.GameType is Campaign)
            {
                CampaignGameStarter campaignStarter = (CampaignGameStarter)gameStarter;
                campaignStarter.AddBehavior(new CompanionsPatrolsBehavior());
            }
        }
    }

    internal class CompanionsPatrolsBehavior : CampaignBehaviorBase
    {
        private const string EvtPatrolLands = "patrol_player_lands";
        private const float MinPatrolDistance = 2000f;
        private const int HoursRequirement = 24;
        /** Should be multiple of HoursRequirement */
        private const int MaxCompanionPersonalRelation = HoursRequirement * 2;

        private void AssignCompanionToPatrolPlayerLands()
        {
            if (Hero.OneToOneConversationHero == null) return;

            if (!Hero.OneToOneConversationHero.GetHeroOccupiedEvents().Contains(EvtPatrolLands))
            {
                Hero.OneToOneConversationHero.AddEventForOccupiedHero(EvtPatrolLands);
            }
        }

        private void CompanionMissionStopPatrolling()
        {
            if (Hero.OneToOneConversationHero == null) return;

            Hero.OneToOneConversationHero.RemoveEventFromOccupiedHero(EvtPatrolLands);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiKeepPatrollingPlayerLands);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickEvent);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.PrisonerTaken.AddNonSerializedListener(this, OnPrisonerTaken);
            CampaignEvents.OnPartyDisbandedEvent.AddNonSerializedListener(this, OnPartyDisbanded);
        }

        private void OnSessionLaunched(CampaignGameStarter campaignStarter)
        {
            campaignStarter.AddPlayerLine("zendzee_companion_start_mission", "hero_main_options", "zendzee_companion_mission_pretalk",
                "Let's talk about improving the position of our clan.",
                ConditionImroveClanPosition, null);
            campaignStarter.AddDialogLine("zendzee_companion_pretalk_patrolling", "zendzee_companion_mission_pretalk", "zendzee_companion_mission",
                "I patrol our lands.",
                ConditionCompaionInPatrol, null);
            campaignStarter.AddDialogLine("zendzee_companion_pretalk", "zendzee_companion_mission_pretalk", "zendzee_companion_mission",
                "{=7EoBCTX0}What do you want me to do?[rb:unsure]",
                ConditionCompaionHaveNoMission, null);
            campaignStarter.AddPlayerLine("zendzee_companion_mission_patrol", "zendzee_companion_mission", "companion_okay",
                "You should patrol our lands to improve relations with notables.",
                ConditionMissionPatrol, AssignCompanionToPatrolPlayerLands);
            campaignStarter.AddPlayerLine("zendzee_companion_mission_stop", "zendzee_companion_mission", "companion_okay",
                "You can stop doing it.",
                ConditionMissionStop, CompanionMissionStopPatrolling);
            campaignStarter.AddPlayerLine("zendzee_nevermind", "zendzee_companion_mission", "companion_okay",
                "{=mdNRYlfS}Nevermind.",
                null, null);
        }

        private bool ConditionImroveClanPosition()
        {
            return Hero.OneToOneConversationHero != null
                && Hero.OneToOneConversationHero.Clan == Clan.PlayerClan;
        }

        private bool ConditionCompaionInPatrol()
        {
            return Hero.OneToOneConversationHero != null
                && Hero.OneToOneConversationHero.GetHeroOccupiedEvents().Contains(EvtPatrolLands);
        }

        private bool ConditionCompaionHaveNoMission()
        {
            return !ConditionMissionStop();
        }

        private bool ConditionMissionStop()
        {
            return ConditionCompaionInPatrol();
        }

        private bool ConditionMissionPatrol()
        {
            return !ConditionCompaionInPatrol() && Hero.OneToOneConversationHero.IsPartyLeader;
        }

        private void OnHourlyTickEvent(MobileParty mobileParty)
        {
            Hero companion = mobileParty.LeaderHero;
            if (mobileParty == MobileParty.MainParty
                || companion == null
                || !mobileParty.IsLordParty
                || companion.Clan != Clan.PlayerClan)
            {
                return;
            }

            Settlement target = mobileParty.TargetSettlement;
            if (mobileParty.DefaultBehavior != AiBehavior.PatrolAroundPoint
                || target.OwnerClan != Clan.PlayerClan)
            {
                return;
            }

            if (mobileParty.Position2D.DistanceSquared(target.Position2D) > MinPatrolDistance)
            {
                //InformationManager.DisplayMessage(new InformationMessage(companion.Name + " too far away from " + target.Name));
                mobileParty.SetMoveGoToSettlement(target);
                return;
            }
            //InformationManager.DisplayMessage(new InformationMessage(companion.Name + " patrolling around " + target.Name));

            int companion_relation;
            foreach(Hero notable in target.Notables)
            {
                companion_relation = CharacterRelationManager.GetHeroRelation(companion, notable) + 1;
                if (companion_relation % HoursRequirement == 0)
                {
                    // reset companion personal relation
                    companion_relation = MBMath.ClampInt(companion_relation, -MaxCompanionPersonalRelation, MaxCompanionPersonalRelation);

                    // increase player relation
                    int oldRelation = notable.GetRelation(Hero.MainHero);
                    ChangeRelationAction.ApplyPlayerRelation(notable, 1, false, false);
                    int newRelation = notable.GetRelation(Hero.MainHero);
                    int relation_change = newRelation - oldRelation;

                    if (relation_change > 0)
                    {
                        TextObject textObject = GameTexts.FindText("str_your_relation_increased_with_notable", null);
                        TextObject heroText = new TextObject();
                        heroText.SetTextVariable("NAME", notable.Name);
                        textObject.SetTextVariable("HERO", heroText);
                        textObject.SetTextVariable("VALUE", newRelation);
                        textObject.SetTextVariable("MAGNITUDE", relation_change);
                        InformationManager.DisplayMessage(new InformationMessage(textObject.ToString()));
                    }
                }
                CharacterRelationManager.SetHeroRelation(companion, notable, companion_relation);
            }
        }

        private void AiKeepPatrollingPlayerLands(MobileParty mobileParty, PartyThinkParams thoughts)
        {
            Hero companion = mobileParty.LeaderHero;
            if (mobileParty == MobileParty.MainParty
                || companion == null
                || !mobileParty.IsLordParty
                || companion.Clan != Clan.PlayerClan)
            {
                return;
            }

            if (mobileParty.Army != null || !companion.GetHeroOccupiedEvents().Contains(EvtPatrolLands))
            {
                return;
            }

            Settlement target = SettlementHelper.FindNearestSettlementToMapPoint(mobileParty, s => s.OwnerClan == Clan.PlayerClan && s.Notables.Any(n => n.GetRelationWithPlayer() < 1));
            if (target == null)
            {
                target = SettlementHelper.FindNearestSettlementToMapPoint(mobileParty, s => s.OwnerClan == Clan.PlayerClan && s.Notables.Any(n => n.GetRelationWithPlayer() < 51));
                if (target == null)
                {
                    target = SettlementHelper.FindNearestSettlementToMapPoint(mobileParty, s => s.OwnerClan == Clan.PlayerClan);
                    if (target == null)
                    {
                        return;
                    }
                }
            }

            AIBehaviorTuple patrol = new AIBehaviorTuple(target, AiBehavior.PatrolAroundPoint, false);
            float weight = 0;
            thoughts.AIBehaviorScores.TryGetValue(patrol, out weight);
            thoughts.AIBehaviorScores[patrol] = weight + 0.6f;
        }

        private void OnArmyCreated(Army army)
        {
            if (Clan.PlayerClan == null || army.Kingdom != Clan.PlayerClan.Kingdom || army.LeaderParty == MobileParty.MainParty)
            {
                return;
            }

            foreach (Hero hero in Clan.PlayerClan.CommanderHeroes)
            {
                if (hero.GetHeroOccupiedEvents().Contains(EvtPatrolLands) && army.Parties.Contains(hero.PartyBelongedTo))
                {
                    hero.PartyBelongedTo.Army = null;
                }
            }
        }

        private void OnPrisonerTaken(PartyBase arg1, Hero hero)
        {
            hero.RemoveEventFromOccupiedHero(EvtPatrolLands);
        }

        private void OnPartyDisbanded(MobileParty mobileParty)
        {
            if (mobileParty.LeaderHero != null && mobileParty.IsLordParty)
            {
                mobileParty.LeaderHero.RemoveEventFromOccupiedHero(EvtPatrolLands);
            }
        }

    }
}