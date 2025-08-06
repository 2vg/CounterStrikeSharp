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

#include "tick_scheduler.h"

namespace counterstrikesharp {

void TickScheduler::schedule(int tick, std::function<void()> callback)
{
    scheduledTasks.enqueue(std::make_pair(tick, callback));
}

std::vector<std::function<void()>> TickScheduler::getCallbacks(int currentTick)
{
    std::vector<std::function<void()>> callbacksToRun;
    std::vector<std::pair<int, std::function<void()>>> allTasks;
    std::pair<int, std::function<void()>> task;

    while (scheduledTasks.try_dequeue(task))
    {
        if (task.first <= currentTick)
        {
            callbacksToRun.push_back(task.second);
        }
        else
        {
            allTasks.push_back(task);
        }
    }

    for (const auto& remainingTask : allTasks)
    {
        scheduledTasks.enqueue(remainingTask);
    }

    return callbacksToRun;
}
} // namespace counterstrikesharp
