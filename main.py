import requests
import re
from bs4 import BeautifulSoup
from enum import Enum
import csv

playerProfileBaseAddress = 'https://bordtennisportalen.dk/SportsResults/Components/WebService1.asmx/GetPlayerProfile'

playerRankingListBaseAddress = 'https://bordtennisportalen.dk/SportsResults/Components/WebService1.asmx/GetPlayerRankingListPoints'

# TODO: Should be built form data om the profile page
# TODO: https://bordtennisportalen.dk/DBTU/Spiller/VisSpiller/#328804,42024
class Seasons(Enum):
   SEASON_20_21 = 42020
   SEASON_21_22 = 42021
   SEASON_22_23 = 42022
   SEASON_23_24 = 42023
   SEASON_24_25 = 42024

class PlayerProfileData:
    def __init__(self, callback_context_key, season_id: int, player_id, get_player_data=True, show_user_profile=True, show_header=False):
        self.callback_context_key = callback_context_key
        self.season_id = season_id
        self.player_id = player_id
        self.get_player_data = get_player_data
        self.show_user_profile = show_user_profile
        self.show_header = show_header

    def to_dict(self):
        """Convert the instance to a dictionary."""
        return {
            "callbackcontextkey": self.callback_context_key,
            "seasonid": self.season_id,
            "playerid": self.player_id,
            "getplayerdata": self.get_player_data,
            "showUserProfile": self.show_user_profile,
            "showheader": self.show_header
        }

class PlayerProfileRankingData:
    def __init__(self, seasonid, playerid, rankinglistid, rankinglistplayerid):
        self.callbackcontextkey	= "93A0FE1C7ECEE3176F67CBF3F964B3DF61029AD09065090C1E2C513EDE5A6DC2152291EFAB40E90FD597017D5343147E"
        self.getplayerdata = True  # Assuming this is a constant value
        self.playerid = playerid
        self.rankinglistid = rankinglistid
        self.rankinglistplayerid = rankinglistplayerid
        self.seasonid = seasonid

    def to_dict(self):
        """Convert the object to a dictionary."""
        return {
            "callbackcontextkey": self.callbackcontextkey,
            "seasonid": self.seasonid,
            "playerid": self.playerid,
            "rankinglistid": self.rankinglistid,
            "rankinglistplayerid": self.rankinglistplayerid,
            "getplayerdata": self.getplayerdata
        }

def getPlayerProfileData(player_profile_payload: PlayerProfileData):
    # Define the headers
    player_profile_headers = {
        'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0',
        'Content-Type': 'application/json; charset=utf-8',
    }

    response = requests.post(playerProfileBaseAddress, json=player_profile_payload.to_dict(), headers=player_profile_headers)
    player_profile_html = response.json()['d'].get('Html')
    soup = BeautifulSoup(player_profile_html, 'html.parser')
    a_element = soup.find('a', {'title': 'Vis opnåede point'})

    if not a_element:
        return None

    # Get the 'onclick' attribute value
    onclick_value = a_element.get('onclick')
    if onclick_value:
        # Extract parameters by finding the parentheses
        start = onclick_value.find('(') + 1
        end = onclick_value.find(')')
        params = onclick_value[start:end].split(',')

        # Strip whitespace from each parameter
        param_list = [param.strip() for param in params]

        player_ranking_data = PlayerProfileRankingData(
            seasonid=param_list[0],  # seasonid from params
            playerid=param_list[1],  # playerid from params
            rankinglistid=param_list[2],  # rankinglistid from params
            rankinglistplayerid=param_list[3]  # rankinglistplayerid from params
        )

        return player_ranking_data
    return None

def getPlayerRankingListData(player_profile_ranking_data: PlayerProfileRankingData):
    # Define the headers
    headers = {
        'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0',
        'Content-Type': 'application/json; charset=utf-8',
    }

    response = requests.post(playerRankingListBaseAddress, json=player_profile_ranking_data.to_dict(), headers=headers)

    player_ranking_html = response.json()['d'].get('Html')

    soup = BeautifulSoup(player_ranking_html, 'html.parser')

    # Find the table
    table = soup.find('table', class_='playerprofilerankingpointstable')

    # Extract table headers
    headers = [header.text.strip() for header in table.find_all('th')]

    # TODO: Optimise which rows are actually needed
    # Extract table rows
    data = []
    for row in table.find_all('tr')[1:]:  # Skip the header row
        cols = row.find_all('td')
        if cols:  # Ensure the row is not empty
            data.append({headers[i]: cols[i].text.strip() for i in range(len(cols))})

    return data if data else None

# TODO: Don't work but would be nice...
def getContextKey():
    # Define the headers
    player_profile_headers = {
        'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0'
    }

    response = requests.get("https://bordtennisportalen.dk/", headers=player_profile_headers)

    soup = BeautifulSoup(response.text, 'lxml')

    # Extract the JavaScript content from the <script> tag
    script_content = soup.find('script').string

    # Use a regular expression to find the value of SR_CallbackContext
    callback_context_match = re.search(r"SR_CallbackContext\s*=\s*'([^']+)'", script_content)

    return callback_context_match.group(1) if callback_context_match else None

if __name__ == '__main__':
    allStats = []

    for season in Seasons:
        playerProfilePayload = PlayerProfileData(
            callback_context_key="93A0FE1C7ECEE3176F67CBF3F964B3DF61029AD09065090C1E2C513EDE5A6DC2022B06A3A7B49B829A6A27791969EF65",
            season_id=season.value,
            player_id="328804"
        )

        print(f"Querying endpoint for data on Season: {season.value}")
        playerData = getPlayerProfileData(playerProfilePayload)

        if playerData is None:
            continue

        rankingData = getPlayerRankingListData(playerData)
        print("Got response. Adding data to the collection")
        allStats += rankingData

    with open('output.csv', 'w', newline='') as file:
        writer = csv.DictWriter(file, fieldnames=allStats[0].keys())
        writer.writeheader()
        writer.writerows(allStats)
