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

#include "core/globals.h"
#include "core/log.h"
#include "core/managers/player_manager.h"
#include "scripting/autonative.h"

namespace counterstrikesharp {

static void* CreateConnectedPlayersIterator(ScriptContext& script_context)
{
    auto iterator = new PlayerManager::ConnectedPlayerIterator(
        globals::playerManager.GetConnectedPlayersIterator());
    return static_cast<void*>(iterator);
}

static bool IteratorHasNext(ScriptContext& script_context)
{
    auto iterator = static_cast<PlayerManager::ConnectedPlayerIterator*>(
        script_context.GetArgument<void*>(0));
    if (iterator == nullptr) return false;
    return iterator->HasNext();
}

static int IteratorGetCurrentSlot(ScriptContext& script_context)
{
    auto iterator = static_cast<PlayerManager::ConnectedPlayerIterator*>(
        script_context.GetArgument<void*>(0));
    if (iterator == nullptr) return -1;
    return iterator->GetCurrentSlot();
}

static void IteratorMoveNext(ScriptContext& script_context)
{
    auto iterator = static_cast<PlayerManager::ConnectedPlayerIterator*>(
        script_context.GetArgument<void*>(0));
    if (iterator != nullptr) {
        iterator->MoveNext();
    }
}

static void DestroyIterator(ScriptContext& script_context)
{
    auto iterator = static_cast<PlayerManager::ConnectedPlayerIterator*>(
        script_context.GetArgument<void*>(0));
    if (iterator != nullptr) {
        delete iterator;
    }
}

REGISTER_NATIVES(players, {
    ScriptEngine::RegisterNativeHandler("CREATE_CONNECTED_PLAYERS_ITERATOR", CreateConnectedPlayersIterator);
    ScriptEngine::RegisterNativeHandler("ITERATOR_HAS_NEXT", IteratorHasNext);
    ScriptEngine::RegisterNativeHandler("ITERATOR_GET_CURRENT_SLOT", IteratorGetCurrentSlot);
    ScriptEngine::RegisterNativeHandler("ITERATOR_MOVE_NEXT", IteratorMoveNext);
    ScriptEngine::RegisterNativeHandler("DESTROY_ITERATOR", DestroyIterator);
})

} // namespace counterstrikesharp