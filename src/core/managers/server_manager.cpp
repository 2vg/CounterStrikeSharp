/*
 *  This file is part of CounterStrikeSharp.
 *  CounterStrikeSharp is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  CounterStrikeSharp is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with CounterStrikeSharp.  If not, see <https://www.gnu.org/licenses/>. *
 */

#include "core/managers/server_manager.h"

#include "core/log.h"
#include "scripting/callback_manager.h"

#include "core/game_system.h"

KHook::Virtual<ISource2Server, void, bool> hibernateHook(&ISource2Server::ServerHibernationUpdate,
                                                         &counterstrikesharp::globals::serverManager,
                                                         &counterstrikesharp::ServerManager::Hook_ServerHibernationUpdate,
                                                         nullptr);

KHook::Virtual<ISource2Server, void> steamApiActivatedHook(&ISource2Server::GameServerSteamAPIActivated,
                                                           &counterstrikesharp::globals::serverManager,
                                                           &counterstrikesharp::ServerManager::Hook_GameServerSteamAPIActivated,
                                                           nullptr);

KHook::Virtual<ISource2Server, void> steamApiDeactivatedHook(&ISource2Server::GameServerSteamAPIDeactivated,
                                                             &counterstrikesharp::globals::serverManager,
                                                             &counterstrikesharp::ServerManager::Hook_GameServerSteamAPIDeactivated,
                                                             nullptr);

KHook::Virtual<ISource2Server, void, const char*> hostnameChangedHook(&ISource2Server::OnHostNameChanged,
                                                                      &counterstrikesharp::globals::serverManager,
                                                                      &counterstrikesharp::ServerManager::Hook_OnHostNameChanged,
                                                                      nullptr);

KHook::Virtual<ISource2Server, void, float> updateNotInGameHook(&ISource2Server::UpdateWhenNotInGame,
                                                                &counterstrikesharp::globals::serverManager,
                                                                &counterstrikesharp::ServerManager::Hook_UpdateWhenNotInGame,
                                                                nullptr);

KHook::Virtual<ISource2Server, void, bool> preWorldUpdateHook(&ISource2Server::PreWorldUpdate,
                                                              &counterstrikesharp::globals::serverManager,
                                                              &counterstrikesharp::ServerManager::Hook_PreWorldUpdate,
                                                              nullptr);

KHook::Virtual<ISource2Server, void> preFatalShutdownHook(&ISource2Server::PreFatalShutdown,
                                                          &counterstrikesharp::globals::serverManager,
                                                          &counterstrikesharp::ServerManager::Hook_PreFatalShutdown,
                                                          nullptr);

namespace counterstrikesharp {

ServerManager::ServerManager() = default;

ServerManager::~ServerManager() = default;

void ServerManager::OnAllInitialized()
{
    hibernateHook.Add(globals::server);
    steamApiActivatedHook.Add(globals::server);
    steamApiDeactivatedHook.Add(globals::server);
    hostnameChangedHook.Add(globals::server);
    updateNotInGameHook.Add(globals::server);
    preWorldUpdateHook.Add(globals::server);
    preFatalShutdownHook.Add(globals::server);

    on_server_hibernation_update_callback = globals::callbackManager.CreateCallback("OnServerHibernationUpdate");
    on_server_steam_api_activated_callback = globals::callbackManager.CreateCallback("OnGameServerSteamAPIActivated");
    on_server_steam_api_deactivated_callback = globals::callbackManager.CreateCallback("OnGameServerSteamAPIDeactivated");
    on_server_hostname_changed_callback = globals::callbackManager.CreateCallback("OnHostNameChanged");
    on_server_pre_fatal_shutdown = globals::callbackManager.CreateCallback("OnPreFatalShutdown");
    on_server_update_when_not_in_game = globals::callbackManager.CreateCallback("OnUpdateWhenNotInGame");
    on_server_pre_world_update = globals::callbackManager.CreateCallback("OnServerPreWorldUpdate");
    on_server_pre_entity_think = globals::callbackManager.CreateCallback("OnServerPreEntityThink");
    on_server_post_entity_think = globals::callbackManager.CreateCallback("OnServerPostEntityThink");

    on_server_precache_resources = globals::callbackManager.CreateCallback("OnServerPrecacheResources");
}

void ServerManager::OnShutdown()
{
    hibernateHook.Remove(globals::server);
    steamApiActivatedHook.Remove(globals::server);
    steamApiDeactivatedHook.Remove(globals::server);
    hostnameChangedHook.Remove(globals::server);
    updateNotInGameHook.Remove(globals::server);
    preWorldUpdateHook.Remove(globals::server);
    preFatalShutdownHook.Remove(globals::server);

    globals::callbackManager.ReleaseCallback(on_server_hibernation_update_callback);
    globals::callbackManager.ReleaseCallback(on_server_steam_api_activated_callback);
    globals::callbackManager.ReleaseCallback(on_server_steam_api_deactivated_callback);
    globals::callbackManager.ReleaseCallback(on_server_hostname_changed_callback);
    globals::callbackManager.ReleaseCallback(on_server_pre_fatal_shutdown);
    globals::callbackManager.ReleaseCallback(on_server_update_when_not_in_game);
    globals::callbackManager.ReleaseCallback(on_server_pre_world_update);
    globals::callbackManager.ReleaseCallback(on_server_pre_entity_think);
    globals::callbackManager.ReleaseCallback(on_server_post_entity_think);

    globals::callbackManager.ReleaseCallback(on_server_precache_resources);
}

void* ServerManager::GetEconItemSystem() { return globals::server->GetEconItemSystem(); }

bool ServerManager::IsPaused() { return globals::server->IsPaused(); }

KHook::Return<void> ServerManager::Hook_ServerHibernationUpdate(ISource2Server* pThis, bool bHibernating)
{
    CSSHARP_CORE_TRACE("Server hibernation update {0}", bHibernating);

    auto callback = globals::serverManager.on_server_hibernation_update_callback;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->ScriptContext().Push(bHibernating);
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

KHook::Return<void> ServerManager::Hook_GameServerSteamAPIActivated(ISource2Server* pThis)
{
    CSSHARP_CORE_TRACE("GameServerSteamAPIActivated");

    auto callback = globals::serverManager.on_server_steam_api_activated_callback;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

KHook::Return<void> ServerManager::Hook_GameServerSteamAPIDeactivated(ISource2Server* pThis)
{
    CSSHARP_CORE_TRACE("GameServerSteamAPIDeactivated");

    auto callback = globals::serverManager.on_server_steam_api_deactivated_callback;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

KHook::Return<void> ServerManager::Hook_OnHostNameChanged(ISource2Server* pThis, const char* pHostname)
{
    CSSHARP_CORE_TRACE("Server hostname changed {0}", pHostname);

    auto callback = globals::serverManager.on_server_hostname_changed_callback;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->ScriptContext().Push(pHostname);
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

KHook::Return<void> ServerManager::Hook_PreFatalShutdown(const ISource2Server* pThis)
{
    CSSHARP_CORE_TRACE("Pre fatal shutdown");

    auto callback = globals::serverManager.on_server_pre_fatal_shutdown;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

KHook::Return<void> ServerManager::Hook_UpdateWhenNotInGame(ISource2Server* pThis, float flFrameTime)
{
    CSSHARP_CORE_TRACE("Update when not in game {}", flFrameTime);

    auto callback = globals::serverManager.on_server_update_when_not_in_game;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->ScriptContext().Push(flFrameTime);
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

KHook::Return<void> ServerManager::Hook_PreWorldUpdate(ISource2Server* pThis, bool bSimulating)
{
    auto callback = globals::serverManager.on_server_pre_world_update;

    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->ScriptContext().Push(bSimulating);
        callback->Execute();
    }

    return { KHook::Action::Ignore };
}

void ServerManager::OnPrecacheResources(IEntityResourceManifest* pResourceManifest)
{
    CSSHARP_CORE_TRACE("Precache resources");
    auto callback = globals::serverManager.on_server_precache_resources;
    if (callback && callback->GetFunctionCount())
    {
        callback->ScriptContext().Reset();
        callback->ScriptContext().Push(pResourceManifest);
        callback->Execute();
    }
}

} // namespace counterstrikesharp
