/**
 * JSON Parser Module
 * Handles parsing of JSON lines with repair for truncated data
 */

class JsonParser {
    constructor() {
        this.repairedCount = 0;
        this.failedCount = 0;
    }

    /**
     * Parse content with one JSON object per line
     * @param {string} content - File content
     * @param {function} progressCallback - Progress callback (message, percent)
     * @returns {Array} Parsed diagnostics objects
     */
    parseLines(content, progressCallback = null) {
        const lines = content.split('\n').filter(line => line.trim());
        const results = [];
        this.repairedCount = 0;
        this.failedCount = 0;

        for (let i = 0; i < lines.length; i++) {
            if (progressCallback && i % 100 === 0) {
                progressCallback(`Parsing line ${i + 1} of ${lines.length}...`, (i / lines.length) * 40);
            }

            const parsed = this.parseLine(lines[i]);
            if (parsed) {
                parsed._rawJson = lines[i].trim();
                parsed._lineNumber = i + 1;
                results.push(parsed);
            }
        }

        return results;
    }

    /**
     * Parse a single line, attempting repair if needed
     * @param {string} line - JSON string
     * @returns {Object|null} Parsed object or null (includes _wasRepaired flag)
     */
    parseLine(line) {
        if (!line || !line.trim()) return null;
        line = line.trim();

        // Try direct parse first
        try {
            const parsed = this.normalizeKeys(JSON.parse(line));
            parsed._wasRepaired = false;
            return parsed;
        } catch (e) {
            // Need repair
        }

        // Attempt repair with multiple iterations
        let repaired = line;
        for (let attempt = 0; attempt < 10; attempt++) {
            repaired = this.repairJson(repaired);
            try {
                const parsed = this.normalizeKeys(JSON.parse(repaired));
                parsed._wasRepaired = true;
                this.repairedCount++;
                return parsed;
            } catch (e) {
                // Continue trying
            }
        }

        this.failedCount++;
        return null;
    }

    /**
     * Repair truncated JSON by closing unclosed structures
     * @param {string} json - Potentially truncated JSON
     * @returns {string} Repaired JSON
     */
    repairJson(json) {
        if (!json) return json;

        // Remove trailing truncation markers
        json = json.replace(/,?\s*\.{3,}\s*$/, '');
        json = json.replace(/,\s*$/, '');

        // Track open structures
        const stack = [];
        let inString = false;
        let escapeNext = false;

        for (let i = 0; i < json.length; i++) {
            const c = json[i];

            if (escapeNext) {
                escapeNext = false;
                continue;
            }

            if (c === '\\' && inString) {
                escapeNext = true;
                continue;
            }

            if (c === '"' && !escapeNext) {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c === '{' || c === '[') {
                stack.push(c);
            } else if (c === '}') {
                if (stack.length > 0 && stack[stack.length - 1] === '{') {
                    stack.pop();
                }
            } else if (c === ']') {
                if (stack.length > 0 && stack[stack.length - 1] === '[') {
                    stack.pop();
                }
            }
        }

        // Close unclosed string
        if (inString) {
            json += '"';
        }

        // Remove trailing incomplete property
        json = json.replace(/,\s*"[^"]*"\s*:\s*$/, '');
        json = json.replace(/,\s*"[^"]*$/, '');

        // Close brackets in reverse order (LIFO)
        while (stack.length > 0) {
            const open = stack.pop();
            json += (open === '{') ? '}' : ']';
        }

        return json;
    }

    /**
     * Normalize keys to consistent camelCase format
     * @param {Object} obj - Object to normalize
     * @returns {Object} Normalized object
     */
    normalizeKeys(obj) {
        if (!obj || typeof obj !== 'object') return obj;

        if (Array.isArray(obj)) {
            return obj.map(item => this.normalizeKeys(item));
        }

        const keyMap = {
            'duration in milliseconds': 'duration',
            'start datetime': 'startTime',
            'Client Side Request Stats': 'clientSideRequestStats',
            'StoreResponseStatistics': 'storeResponseStatistics',
            'AddressResolutionStatistics': 'addressResolutionStatistics',
            'HttpResponseStats': 'httpResponseStats',
            'transportRequestTimeline': 'transportRequestTimeline',
            'requestTimeline': 'requestTimeline',
            'DurationInMs': 'durationInMs',
            'ResourceType': 'resourceType',
            'OperationType': 'operationType',
            'StatusCode': 'statusCode',
            'SubStatusCode': 'subStatusCode',
            'StoreResult': 'storeResult',
            'StorePhysicalAddress': 'storePhysicalAddress',
            'BELatencyInMs': 'beLatencyInMs',
            'TransportException': 'transportException',
            'ResponseTimeUTC': 'responseTimeUtc',
            'LocationEndpoint': 'locationEndpoint',
            'RequestSessionToken': 'requestSessionToken',
            'ActivityId': 'activityId'
        };

        const normalized = {};
        for (const key of Object.keys(obj)) {
            const newKey = keyMap[key] || key;
            normalized[newKey] = this.normalizeKeys(obj[key]);
        }
        return normalized;
    }

    /**
     * Get parsing statistics
     * @returns {Object} Stats object
     */
    getStats() {
        return {
            repaired: this.repairedCount,
            failed: this.failedCount
        };
    }
}

// Export for browser
window.JsonParser = JsonParser;
