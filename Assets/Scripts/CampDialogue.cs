using System.Collections.Generic;

// Speaker and visitor use ChampionSpawner's roster order:
// Hyde, Rolling, Scrat, Rero Rero, Elsa, Hulk.
// References inform personality and speech only. Subjects belong to this camp;
// do not import a reference character's occupation, hobbies, history, or catchphrases.
public static class CampDialogue
{
    const int RosterSize = 6;
    static readonly Dictionary<int, string[]> Conversations = new Dictionary<int, string[]>
    {
        // Hyde / Shirou Emiya: earnest help and self-neglect, with ordinary tastes and hopes of his own.
        { 0 * RosterSize + 1, new[] {
            "I found the blanket you left by my door. It helped last night.",
            "You said you had a spare, but your bed looked pretty bare this morning.",
            "I'll bring it back before dark. We can find another together." } },
        { 0 * RosterSize + 2, new[] {
            "I tried making bread with acorn flour. It's a little bitter.",
            "Don't look like that. I used the cracked ones you threw away.",
            "Here, taste it. If it's bad, we'll tell everyone it was my idea." } },
        { 0 * RosterSize + 3, new[] {
            "Rain upstairs, I think. The water coming through feels colder.",
            "I used to leave the window open when it rained. Even in winter.",
            "If we find a way out, I'd like one quiet day where nothing needs fixing." } },
        { 0 * RosterSize + 4, new[] {
            "One of the miners left his lunch beside a hook. Two little sandwiches.",
            "I kept thinking someone must have packed those for him.",
            "I know he was trying to catch us. I still put his lunch somewhere dry." } },
        { 0 * RosterSize + 5, new[] {
            "You asked what I'd do if we got out. I hadn't really thought about it.",
            "I'd like to walk somewhere without counting who's still behind me.",
            "That probably sounds small. It sounds good to me." } },
        // Rolling / Eriri Sawamura: proud, competitive, sensitive to criticism; care is easier to give than admit.
        { 1 * RosterSize + 0, new[] {
            "You've got mud behind your ear. No, the other one.",
            "Stop rubbing it! You're spreading it. Hold still, I'll get it.",
            "There. Honestly, does nobody check you before you leave your wallow?" } },
        { 1 * RosterSize + 2, new[] {
            "Rain again. I can tell because you're dripping on my floor.",
            "There's a dry cushion behind me. Go on, before your tail soaks this one.",
            "The acorn gets a cushion too? Fine. Apparently I run an inn now." } },
        { 1 * RosterSize + 3, new[] {
            "I heard a hook outside last night. Turned out to be a loose lantern.",
            "I'd already packed everything. Even the stupid cushion.",
            "Don't tell the others. I want one breakfast without being fussed over." } },
        { 1 * RosterSize + 4, new[] {
            "Everyone liked your idea for dinner. I suggested it last week.",
            "No, I'm glad we're doing it. I just wish someone had heard me then.",
            "Save me the seat beside you. I'll help set things out." } },
        { 1 * RosterSize + 5, new[] {
            "Move your foot. My yarn rolled under it.",
            "Don't kick it! I'll end up chasing it all the way to the... oh, come ON!",
            "Roll it back. Slower this time. And stop looking so pleased." } },
        // Scrat / Ice Age: wordless acorn obsession and physical comedy, plus hunger, sleep, shelter, and fleeting connection.
        { 2 * RosterSize + 0, new[] {
            "[Sniffs an empty hole. Points at your snout, then urgently at the dirt.]",
            "[Follows you to another hole. His buried acorn is exactly where you stop.]",
            "[Delighted, he leads you to a clearing covered in dozens of tiny holes.]" } },
        { 2 * RosterSize + 1, new[] {
            "[Pulls a length of yarn toward his stump. Notices your paw on the end.]",
            "[Tugs. You hold firm. He puts down an acorn as an offer.]",
            "[You release the yarn. He grabs both prizes and trips over the string.]" } },
        { 2 * RosterSize + 3, new[] {
            "[A ceiling drip lands on his acorn. He moves it. Another drip follows.]",
            "[Looks up, offended. Holds a leaf over the acorn like an umbrella.]",
            "[You offer a larger leaf. He crawls under it with you, still glaring up.]" } },
        { 2 * RosterSize + 4, new[] {
            "[Asleep against his stump, he runs in place. His paws clutch nothing.]",
            "[Wakes with a squeak. Checks his acorn, then checks it a second time.]",
            "[Settles beside your scarf. One paw stays on the acorn as his eyes close.]" } },
        { 2 * RosterSize + 5, new[] {
            "[Finds a washer from a miner's hook. Rolls it between his paws.]",
            "[Threads it over his acorn. It fits. He proudly holds it up to you.]",
            "[Tries to take it off. Pulls harder. His proud expression disappears.]" } },
        // Rero / Utena + Touga: protective idealism, cultivated charm, and mixed motives; no borrowed prince identity.
        { 3 * RosterSize + 0, new[] {
            "I've been trying to eat politely when we have company.",
            "Then a fly crossed the table. I caught it before I finished apologizing.",
            "Your bowl survived. I hope that counts for something." } },
        { 3 * RosterSize + 1, new[] {
            "You don't have to announce a reason every time you sit by the pond.",
            "You may simply like it here. I'll survive the uncertainty.",
            "The flat stone stays warm longest. I kept it free for you." } },
        { 3 * RosterSize + 2, new[] {
            "I used to fold leaves into little boats. Never made one that lasted.",
            "This one has survived two laps of the pond. A personal triumph.",
            "You may ride along, but your acorn will have to sit in the middle." } },
        { 3 * RosterSize + 4, new[] {
            "Someone has been leaving little stones along the darkest path.",
            "I was going to offer to guide you. You'd already found your own way.",
            "Would you show me? I'd like to see where it leads." } },
        { 3 * RosterSize + 5, new[] {
            "There are old tally marks behind my pond. Too high for me to reach.",
            "Someone was counting days here before we arrived.",
            "I'd like to believe they stopped because they left. Don't you?" } },
        // Elsa / Anon Chihaya: outgoing and image-conscious, insecure but proactive; likes company and making occasions.
        { 4 * RosterSize + 0, new[] {
            "We should have a proper dinner night. No equipment on the table.",
            "I'll handle the invitations. Yes, I know everyone lives ten steps away.",
            "An invitation makes it an occasion! You're doing the soup, right?" } },
        { 4 * RosterSize + 1, new[] {
            "I tried sliding down that icy slope. You should've seen the speed!",
            "Actually, good thing you didn't. I landed beak-first in a snowdrift.",
            "Come with me tomorrow? You can laugh, but help me up first." } },
        { 4 * RosterSize + 2, new[] {
            "I made up a song for counting supplies. It helps, honestly.",
            "Lanterns, blankets, biscuits... Oh, I forgot what rhymes with blankets.",
            "You're already asleep? Wow. Tough audience." } },
        { 4 * RosterSize + 3, new[] {
            "Does the air feel damp to you? My scarf never quite dries down here.",
            "I miss putting clothes outside and having them smell like sunshine.",
            "We should save a good spot for that. If we find the surface, I mean." } },
        { 4 * RosterSize + 5, new[] {
            "Those miners' helmets are awful. You can't even see their eyes.",
            "I'd feel braver if I could tell whether they were scared too.",
            "You don't have to answer. I just wanted to say that out loud." } },
        // Hulk / Jules Winnfield: measured authority and dry humor; food, rituals, restraint, and unvarnished observations.
        { 5 * RosterSize + 0, new[] {
            "That stew needs pepper. Don't argue until you've tried it.",
            "There. See? A warm meal deserves more than hot water and optimism.",
            "Leave the pot. I'll wash it. You've been on your feet all day." } },
        { 5 * RosterSize + 1, new[] {
            "I came to this corner for a little quiet. Hear that echo?",
            "Can't clear my throat without the whole damn cave joining in.",
            "You found a quiet place to sleep? Show me. I'll bring my own cushion." } },
        { 5 * RosterSize + 2, new[] {
            "You hear that hook scrape before the miner turns the corner?",
            "That's when you leave. Your legs work fine before you're frightened.",
            "I'll carry the acorn if that's what it takes. We both get out." } },
        { 5 * RosterSize + 3, new[] {
            "Found a deck of cards in a supply crate. Three queens, no kings.",
            "Makes for an interesting game, if you don't get precious about rules.",
            "Bring something to drink. We'll argue about the rules as we go." } },
        { 5 * RosterSize + 4, new[] {
            "I stood by the gate this morning. Thought I felt a breeze.",
            "Could've been nothing. Stood there a while anyway.",
            "If it's there tomorrow, I'll come get you. Bring that stubborn scarf." } },
    };

    public static string GetLine(int speaker, int visitor, int turn)
    {
        if (speaker < 0 || speaker >= RosterSize || visitor < 0 || visitor >= RosterSize || turn < 0)
            return string.Empty;
        string[] lines;
        return Conversations.TryGetValue(speaker * RosterSize + visitor, out lines)
            ? lines[turn % lines.Length] : string.Empty;
    }
}
